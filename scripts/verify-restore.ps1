param(
    [Parameter(Mandatory = $true)][string]$BackupFolder,
    [int]$Port = 3002
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$backup = (Resolve-Path -LiteralPath $BackupFolder).Path
if (!(Test-Path -LiteralPath (Join-Path $backup 'database.dump'))) { throw 'Backup must contain database.dump.' }
if (!(Test-Path -LiteralPath (Join-Path $backup 'uploads'))) { throw 'Backup must contain uploads.' }
$project = 'rabbithole-verify-' + (Get-Date -Format 'yyyyMMddHHmmss')
$cache = Join-Path $repo '.cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$override = Join-Path $cache "$project.yaml"
@'
services:
  api:
    image: rabbithole-api
  web:
    image: rabbithole-web
'@ | Set-Content -LiteralPath $override
$compose = @('compose', '-p', $project, '-f', (Join-Path $repo 'compose.yaml'), '-f', $override)
function Compose {
    & docker @compose @args
    if ($LASTEXITCODE -ne 0) { throw "Docker failed: $args" }
}
function Api([string]$Path, [string]$Method = 'GET', $Data = $null) {
    $options = @{ Uri = "http://localhost:$Port/api$Path"; Method = $Method; TimeoutSec = 60 }
    if ($null -ne $Data) { $options.ContentType = 'application/json'; $options.Body = ConvertTo-Json $Data -Depth 10 }
    Invoke-RestMethod @options
}
$oldPort = $env:PORT
$created = $false
try {
    $projects = docker compose ls --all --format json | ConvertFrom-Json
    if ($projects.Name -contains $project) { throw 'Verification project already exists. Nothing was changed.' }
    $env:PORT = [string]$Port
    $created = $true
    Compose up -d --wait db
    Compose cp (Join-Path $backup 'database.dump') db:/tmp/restore.dump
    Compose exec -T db pg_restore -U rabbithole -d rabbithole --no-owner /tmp/restore.dump
    Compose create --no-build api
    Compose cp (Join-Path $backup 'uploads/.') api:/data/uploads
    Compose up -d --wait --no-build
    $catalog = Api '/items?limit=100'
    if ($catalog.total -lt 1) { throw 'This verification needs a backup with at least one real imported item.' }
    $itemId = $catalog.items[0].id
    $null = Api "/items/$itemId/state" 'PATCH' @{ saved = $true; read = $true; progress = 0.42 }
    $note = Api "/items/$itemId/notes" 'POST' @{ body = 'RESTORE VERIFICATION: private note survives restart' }
    $hole = Api '/holes' 'POST' @{ itemId = $itemId; title = 'RESTORE VERIFICATION trail' }
    $root = $hole.nodes[0].id
    $null = Api "/holes/$($hole.id)/nodes" 'POST' @{ parentId = $root; question = 'RESTORE VERIFICATION branch A' }
    $second = Api "/holes/$($hole.id)/nodes" 'POST' @{ parentId = $root; question = 'RESTORE VERIFICATION branch B' }
    $search = Api '/search?q=VERIFICATION'
    if ($search.items.id -notcontains $itemId) { throw 'PostgreSQL search did not find the saved note.' }
    $pdfPath = Join-Path $repo 'tests/e2e/fixtures/test-page.pdf'
    $uploadText = curl.exe --silent --show-error --fail-with-body -F "file=@$pdfPath;type=application/pdf" "http://localhost:$Port/api/library/books/upload"
    if ($LASTEXITCODE -ne 0) { throw 'PDF upload failed.' }
    $upload = $uploadText | ConvertFrom-Json
    $bookId = $upload.document.id
    $books = Api '/library/books'
    $book = $books | Where-Object { $_.id -eq $bookId }
    if ($book.status -ne 'ready' -or $book.pageCount -ne 1) { throw 'Linux PDF parser did not produce a real page.' }
    Compose restart db api web
    Compose up -d --wait --no-build
    $item = Api "/items/$itemId"
    if (!$item.state.saved -or !$item.state.read -or $item.state.progress -ne 0.42) { throw 'Saved/read/progress state was lost.' }
    if ($item.notes.id -notcontains $note.id) { throw 'Personal note was lost.' }
    $trail = Api "/holes/$($hole.id)"
    if ($trail.nodes.Count -ne 3 -or $trail.lastNodeId -ne $second.id) { throw 'Branch or navigation state was lost.' }
    $download = Join-Path $cache "$project.pdf"
    Invoke-WebRequest "http://localhost:$Port/api/library/books/$bookId/file" -OutFile $download
    if ((Get-FileHash -LiteralPath $pdfPath).Hash -ne (Get-FileHash -LiteralPath $download).Hash) { throw 'Original PDF changed or was lost.' }
    $export = Api '/export' 'POST'
    $exportText = $export | ConvertTo-Json -Depth 30
    if ($exportText -notmatch 'RESTORE VERIFICATION' -or $exportText -match '"apiKey"|"password"|"storageName"') { throw 'Export lost data or exposed private configuration.' }
    Write-Output "PASS: restored $($catalog.total) real items; PostgreSQL note search; saved/read/progress; sibling branches and position; Linux PDF parser; original upload hash; export; full database/API/web restart."
} finally {
    # This unique project was created above. Never clean up the main rabbithole project.
    if ($created -and $project -match '^rabbithole-verify-\d{14}$') {
        & docker @compose down --volumes
    }
    $env:PORT = $oldPort
}
