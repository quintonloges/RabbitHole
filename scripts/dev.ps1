param([ValidateSet('start', 'stop', 'restart', 'status', 'logs')][string]$Action = 'start')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment-common.ps1')
$compose = @(Get-RabbitHoleCompose dev)
switch ($Action) {
    'start' { Invoke-RabbitHoleCompose $compose @('up', '--build', '-d', '--wait') }
    'stop' { Invoke-RabbitHoleCompose $compose @('stop') }
    'restart' { Invoke-RabbitHoleCompose $compose @('restart') }
    'status' { Invoke-RabbitHoleCompose $compose @('ps') }
    'logs' { Invoke-RabbitHoleCompose $compose @('logs', '--tail', '100') }
}
