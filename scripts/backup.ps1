param([string]$Destination = (Join-Path $PSScriptRoot '../backups'))
$ErrorActionPreference = 'Stop'
$folder = Join-Path ([IO.Path]::GetFullPath($Destination)) (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $folder -Force | Out-Null
Push-Location (Join-Path $PSScriptRoot '..')
try {
    # Stop writers for a consistent database/files pair. Database stays online.
    docker compose stop api
    if ($LASTEXITCODE -ne 0) { throw 'Could not stop API.' }
    docker compose exec -T db pg_dump -U rabbithole -d rabbithole -Fc -f /tmp/rabbithole.dump
    if ($LASTEXITCODE -ne 0) { throw 'Database backup failed.' }
    docker compose cp db:/tmp/rabbithole.dump (Join-Path $folder 'database.dump')
    if ($LASTEXITCODE -ne 0) { throw 'Could not copy database backup.' }
    docker compose cp api:/data/uploads (Join-Path $folder 'uploads')
    if ($LASTEXITCODE -ne 0) { throw 'Upload backup failed.' }
    Write-Output "Backup saved to $folder. Keep your private .env separately; it is not included."
} finally {
    docker compose start api
    Pop-Location
}
