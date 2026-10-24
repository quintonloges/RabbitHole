param(
    [string]$Destination = (Join-Path $PSScriptRoot '../backups'),
    [ValidateSet('dev', 'production')][string]$Environment = 'production'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment-common.ps1')
$compose = @(Get-RabbitHoleCompose $Environment)
$folder = Join-Path ([IO.Path]::GetFullPath($Destination)) ((Get-Date -Format 'yyyyMMdd-HHmmss') + "-$Environment")
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$running = @(Invoke-RabbitHoleCompose $compose @('ps', '--status', 'running', '--services'))
$resumeApi = $running -contains 'api'
try {
    # Stop writers for a consistent database/files pair. Database stays online.
    if ($resumeApi) { Invoke-RabbitHoleCompose $compose @('stop', 'api') }
    Invoke-RabbitHoleCompose $compose @('exec', '-T', 'db', 'pg_dump', '-U', 'rabbithole', '-d', 'rabbithole', '-Fc', '-f', '/tmp/rabbithole.dump')
    Invoke-RabbitHoleCompose $compose @('cp', 'db:/tmp/rabbithole.dump', (Join-Path $folder 'database.dump'))
    Invoke-RabbitHoleCompose $compose @('cp', 'api:/data/uploads', (Join-Path $folder 'uploads'))
    Write-Output "$Environment backup saved to $folder. Keep private deployment settings separately; they are not included."
} finally {
    if ($resumeApi) { Invoke-RabbitHoleCompose $compose @('start', 'api') }
}
