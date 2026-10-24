param([ValidateSet('start', 'stop', 'restart', 'status', 'logs')][string]$Action = 'status')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment-common.ps1')
$compose = @(Get-RabbitHoleCompose production)
switch ($Action) {
    # No build context or mutable image tag can enter this deployment.
    'start' { Invoke-RabbitHoleCompose $compose @('up', '-d', '--wait', '--no-build', '--pull', 'never') }
    'stop' { Invoke-RabbitHoleCompose $compose @('stop') }
    'restart' { Invoke-RabbitHoleCompose $compose @('restart') }
    'status' { Invoke-RabbitHoleCompose $compose @('ps') }
    'logs' { Invoke-RabbitHoleCompose $compose @('logs', '--tail', '100') }
}
