$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$checkProject = 'rabbithole-release-check-' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$checkCompose = @('compose', '--project-name', $checkProject, '--file', (Join-Path $root 'tests/release/compose.yaml'))
$checkArtifacts = Join-Path $root ".cache/release-check/$checkProject/test-results"
New-Item -ItemType Directory -Force -Path $checkArtifacts | Out-Null

function Invoke-ReleaseCheck([string[]]$Arguments) {
    & docker @checkCompose @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Local release check failed: docker compose $($Arguments -join ' ')" }
}

Push-Location $root
try {
    & docker info --format '{{.ServerVersion}}'
    if ($LASTEXITCODE -ne 0) { throw 'Docker Desktop with Linux containers is required for release preflight.' }
    Write-Host "Running full Linux release validation in disposable project $checkProject."
    Invoke-ReleaseCheck @('run', '--rm', '--no-deps', 'workflows')
    Invoke-ReleaseCheck @('build', 'checks')
    Invoke-ReleaseCheck @('up', '-d', '--wait', 'db')
    # Keep browser screenshots/traces when the disposable test container is removed.
    Invoke-ReleaseCheck @('run', '--rm', '--volume', "${checkArtifacts}:/src/test-results", 'checks')
    Invoke-ReleaseCheck @('run', '--rm', '--no-deps', 'caddy-public')
    Invoke-ReleaseCheck @('run', '--rm', '--no-deps', 'caddy-staging')
    Write-Host 'PASS: local release validation completed, including browser, PostgreSQL and Linux backup checks.'
} finally {
    # Only this invocation's generated project can be removed. No personal volumes are mounted.
    if ($checkProject -notmatch '^rabbithole-release-check-[a-f0-9]{12}$') { throw 'Refusing cleanup of an unexpected project.' }
    & docker @checkCompose down --volumes --remove-orphans --rmi local
    if ($LASTEXITCODE -ne 0) { Write-Warning "Could not clean up disposable project $checkProject." }
    Pop-Location
}
