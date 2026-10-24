param(
    [string]$Destination = (Join-Path $PSScriptRoot '../backups'),
    [ValidateSet('dev', 'production')][string]$Environment = 'production',
    [switch]$KeepStopped
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment-common.ps1')
$compose = @(Get-RabbitHoleCompose $Environment)
$folder = Join-Path ([IO.Path]::GetFullPath($Destination)) ((Get-Date -Format 'yyyyMMdd-HHmmss') + "-$Environment")
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$running = @(Invoke-RabbitHoleCompose $compose @('ps', '--status', 'running', '--services'))
$resumeApi = $running -contains 'api'
$startedDb = $running -notcontains 'db'
try {
    # Stop writers for a consistent database/files pair. Database stays online.
    if ($resumeApi) { Invoke-RabbitHoleCompose $compose @('stop', 'api') }
    if ($startedDb) { Invoke-RabbitHoleCompose $compose @('up', '-d', '--wait', '--no-build', '--pull', 'never', 'db') }
    Invoke-RabbitHoleCompose $compose @('exec', '-T', 'db', 'pg_dump', '-U', 'rabbithole', '-d', 'rabbithole', '-Fc', '-f', '/tmp/rabbithole.dump')
    Invoke-RabbitHoleCompose $compose @('cp', 'db:/tmp/rabbithole.dump', (Join-Path $folder 'database.dump'))
    Invoke-RabbitHoleCompose $compose @('cp', 'api:/data/uploads', (Join-Path $folder 'uploads'))
    python (Join-Path $PSScriptRoot 'preservation-manifest.py') --output (Join-Path $folder 'manifest.json') --uploads (Join-Path $folder 'uploads') -- docker @compose exec -T db psql -U rabbithole -d rabbithole -Atqc
    if ($LASTEXITCODE -ne 0) { throw 'Preservation manifest failed; backup is not verified.' }
    if ($Environment -eq 'dev') { Invoke-RabbitHoleCompose $compose @('cp', 'api:/data/auth-keys', (Join-Path $folder 'auth-keys')) }
    Write-Output "$Environment backup saved to $folder. Keep private deployment settings separately; they are not included."
} finally {
    # Remove only this backup's temporary dump inside the selected database container.
    & docker @compose exec -T db rm -f /tmp/rabbithole.dump
    if ($LASTEXITCODE -ne 0) { Write-Warning 'Temporary dump cleanup failed; inspect /tmp/rabbithole.dump in the selected database container.' }
    if ($KeepStopped) { Invoke-RabbitHoleCompose $compose @('stop') }
    else {
        if ($resumeApi) { Invoke-RabbitHoleCompose $compose @('start', 'api') }
        if ($startedDb) { Invoke-RabbitHoleCompose $compose @('stop', 'db') }
    }
}
