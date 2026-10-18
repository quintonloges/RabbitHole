$ErrorActionPreference = 'Stop'
$checkDatabase = 'rabbithole_recommendation_test_' + (Get-Date -Format 'yyyyMMddHHmmss')
$priorCheckDatabase = $env:RABBITHOLE_CHECK_DATABASE
$env:RABBITHOLE_CHECK_DATABASE = $checkDatabase
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    # Snapshot and migrate a disposable copy on the actual PostgreSQL server.
    # No source refresh, AI call, fixture row or state mutation reaches production.
    docker compose exec -T db pg_dump -U rabbithole -d rabbithole -Fc -f /tmp/recommendation-check.dump
    if ($LASTEXITCODE -ne 0) { throw 'Snapshot failed.' }
    docker compose exec -T db createdb -U rabbithole $checkDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated verification database.' }
    docker compose exec -T db pg_restore -U rabbithole --no-owner --no-privileges -d $checkDatabase /tmp/recommendation-check.dump
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore verification database.' }
    docker compose -f compose.yaml -f tests/recommendations/compose.yaml run --build --rm --no-deps recommendation-checks
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL checks failed. Inspect the isolated database $checkDatabase." }
    if ($checkDatabase -notmatch '^rabbithole_recommendation_test_[0-9]{14}$') { throw 'Refusing cleanup of unexpected database name.' }
    docker compose exec -T db dropdb -U rabbithole $checkDatabase
    if ($LASTEXITCODE -ne 0) { throw 'Checks passed, but disposable database cleanup failed.' }
    Write-Output 'PostgreSQL migration and recommendation HTTP checks passed; disposable database removed.'
} finally {
    $env:RABBITHOLE_CHECK_DATABASE = $priorCheckDatabase
    Pop-Location
}
