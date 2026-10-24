param(
    [Parameter(Mandatory = $true)][string]$BackupFolder,
    [int]$Port = 47834,
    [switch]$Keep
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backup = (Resolve-Path -LiteralPath $BackupFolder).Path
foreach ($required in @('database.dump', 'uploads')) {
    if (!(Test-Path -LiteralPath (Join-Path $backup $required))) { throw "Backup missing $required" }
}
$project = 'rabbithole-migrate-' + (Get-Date -Format 'yyyyMMddHHmmss')
$cache = Join-Path $repo '.cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$override = Join-Path $cache "$project.json"
@{ services = @{
    api = @{ image = 'rabbithole-dev-api'; pull_policy = 'never'; environment = @{
        Ingestion__Paused = 'true'; AI__BaseUrl = ''; AI__Model = ''; AI__ApiKey = ''; OpenAlex__ApiKey = ''
        Security__PublicRequestsPerMinute = '100000'; Security__SearchRequestsPerMinute = '100000'
        Logging__LogLevel__Default = 'Warning'
    } }
    web = @{ image = 'rabbithole-dev-web'; pull_policy = 'never'; environment = @{ PUBLIC_INDEXING_ENABLED = 'false' } }
} } | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $override
$compose = @('compose', '-p', $project, '--project-directory', $repo, '--env-file', (Join-Path $repo '.env'), '-f', (Join-Path $repo 'compose.yaml'), '-f', $override)
function Compose {
    & docker @compose @args
    if ($LASTEXITCODE -ne 0) { throw "Rehearsal command failed: $args" }
}
function Manifest([string]$Output, [string]$Baseline = '') {
    $arguments = @((Join-Path $PSScriptRoot 'preservation-manifest.py'), '--output', $Output, '--uploads', (Join-Path $backup 'uploads'))
    if ($Baseline) { $arguments += @('--baseline', $Baseline) }
    & python @arguments -- docker @compose exec -T db psql -U rabbithole -d rabbithole -Atqc
    if ($LASTEXITCODE -ne 0) { throw 'Record preservation comparison failed.' }
}
$oldPort = $env:PORT
$created = $false
try {
    $projects = docker compose ls --all --format json | ConvertFrom-Json
    if ($projects.Name -contains $project) { throw 'Rehearsal project exists; nothing changed.' }
    $env:PORT = [string]$Port
    $created = $true
    Compose up -d --wait db
    Compose cp (Join-Path $backup 'database.dump') db:/tmp/restore.dump
    Compose exec -T db pg_restore -U rabbithole -d rabbithole --no-owner --exit-on-error /tmp/restore.dump
    $before = Join-Path $cache "$project-before.json"
    Manifest $before
    if (!(Test-Path -LiteralPath (Join-Path $backup 'manifest.json'))) { Copy-Item -LiteralPath $before -Destination (Join-Path $backup 'manifest.json') }
    Compose create --no-build api
    Compose cp (Join-Path $backup 'uploads/.') api:/data/uploads
    Compose run --rm --no-deps --user root --entrypoint chown api -R app:app /data/uploads /data/auth-keys
    Compose up -d --wait --no-build --pull never
    $after = Join-Path $cache "$project-after.json"
    Manifest $after $before
    $base = "http://127.0.0.1:$Port"
    $public = Invoke-RestMethod "$base/api/public/items?pageSize=100" -TimeoutSec 120
    foreach ($path in @('/api/items', '/api/settings', '/api/library/books', '/api/holes', '/openapi/v1.json')) {
        $response = Invoke-WebRequest "$base$path" -SkipHttpErrorCheck -TimeoutSec 60
        if ($response.StatusCode -ne 401) { throw "Anonymous private access not denied: $path" }
    }
    $html = Invoke-WebRequest "$base/" -TimeoutSec 120
    if ($html.StatusCode -ne 200) { throw 'Public server-rendered catalog failed.' }
    $publicFields = @('id','title','canonicalUrl','author','publishedAt','ingestedAt','type','provider','sourceName','excerpt','topics','embedUrl','imageUrl')
    $publicIds = [Collections.Generic.HashSet[string]]::new()
    $page = 1
    do {
        foreach ($item in $public.items) {
            if (@($item.PSObject.Properties.Name | Where-Object { $_ -notin $publicFields }).Count) { throw 'Unapproved public field.' }
            if (!$publicIds.Add($item.id)) { throw 'Duplicate public ID across pages.' }
        }
        if ($publicIds.Count -ge $public.total) { break }
        $page++
        $public = Invoke-RestMethod "$base/api/public/items?pageSize=100&page=$page" -TimeoutSec 120
        if (!$public.items.Count) { throw 'Public pagination stopped early.' }
    } while ($true)
    $privateIdsJson = Compose exec -T db psql -U rabbithole -d rabbithole -Atqc 'SELECT coalesce(json_agg("Id"), ''[]''::json) FROM "Items" WHERE "DocumentId" IS NOT NULL OR "Provider" = ''local-upload'';'
    $privateIds = @($privateIdsJson | ConvertFrom-Json)
    foreach ($id in $privateIds) { if ($publicIds.Contains($id)) { throw "Private document in paginated public results: $id" } }
    foreach ($id in ($privateIds | Select-Object -First 30)) {
        $response = Invoke-WebRequest "$base/api/public/items/$id" -SkipHttpErrorCheck -TimeoutSec 60
        if ($response.StatusCode -ne 404) { throw "Document-derived item was publicly reachable: $id" }
    }
    Compose restart db api web
    Compose up -d --wait --no-build --pull never
    Manifest (Join-Path $cache "$project-restart.json") $before
    $verifyUploads = Join-Path $cache "$project-uploads"
    Compose cp api:/data/uploads $verifyUploads
    $expected = (Get-Content -LiteralPath $before -Raw | ConvertFrom-Json).files
    foreach ($file in $expected.PSObject.Properties) {
        $target = Join-Path $verifyUploads $file.Name
        if (!(Test-Path -LiteralPath $target) -or (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.Value) {
            throw "Uploaded file differs after restart: $($file.Name)"
        }
    }
    $report = @{ passed = $true; project = $project; publicItems = $public.total; protectedDocumentItems = $privateIds.Count; manifest = $after; uploadedFiles = @($expected.PSObject.Properties).Count; restart = $true }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $cache 'latest-migration-rehearsal.json')
    Write-Output "PASS: full original library upgrade; $($public.total) public items; $($privateIds.Count) private document items; all original records/files retained through restart."
} finally {
    if ($created -and !$Keep -and $project -match '^rabbithole-migrate-\d{14}$') { & docker @compose down --volumes }
    $env:PORT = $oldPort
}
