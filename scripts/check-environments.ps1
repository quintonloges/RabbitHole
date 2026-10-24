# Checks live isolation and persists temporary data across a development restart.
# Production is read only. The temporary development topic/trail are removed afterward.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment-common.ps1')
$dev = @(Get-RabbitHoleCompose dev)
$prod = @(Get-RabbitHoleCompose production)
$devConfig = Invoke-RabbitHoleCompose $dev @('config', '--format', 'json') | ConvertFrom-Json
$prodConfig = Invoke-RabbitHoleCompose $prod @('config', '--format', 'json') | ConvertFrom-Json
$devPort = $devConfig.services.gateway.ports[0].published
$prodPort = $prodConfig.services.gateway.ports[0].published
if ($devPort -eq $prodPort -or $devPort -eq '3000' -or $prodPort -eq '3000') { throw 'Expected distinct, non-3000 ports.' }
foreach ($config in @($devConfig, $prodConfig)) {
    if ($config.services.gateway.ports[0].host_ip -ne '127.0.0.1') { throw 'Gateway must remain loopback only.' }
    foreach ($service in @('db', 'api', 'web')) {
        if ($config.services.$service.ports) { throw 'Only the gateway may publish a host port.' }
    }
}
foreach ($service in @('db', 'api', 'web', 'gateway')) {
    $prodId = Invoke-RabbitHoleCompose $prod @('ps', '-q', $service)
    $devId = Invoke-RabbitHoleCompose $dev @('ps', '-q', $service)
    if (!$prodId -or !$devId -or $prodId -eq $devId) { throw "Missing or shared $service container." }
    $p = (& docker inspect $prodId | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect production container.' }
    $d = (& docker inspect $devId | ConvertFrom-Json)[0]
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect development container.' }
    if ($p.Config.Labels.'com.docker.compose.project' -ne 'rabbithole' -or
        $d.Config.Labels.'com.docker.compose.project' -ne 'rabbithole-dev') { throw 'Unexpected container project.' }
    if ($p.Image -ne $prodConfig.services.$service.image) { throw "Production $service image changed." }
    $sharedNetworks = @($p.NetworkSettings.Networks.PSObject.Properties.Name | Where-Object { $d.NetworkSettings.Networks.PSObject.Properties.Name -contains $_ })
    if ($sharedNetworks.Count -ne 0) { throw 'Development and production share a network.' }
    $volumeName = switch ($service) { 'db' { 'database' }; 'api' { 'uploads' } }
    if ($volumeName) {
        $productionVolumes = @($p.Mounts | Where-Object Type -eq 'volume' | Select-Object -ExpandProperty Name)
        $developmentVolumes = @($d.Mounts | Where-Object Type -eq 'volume' | Select-Object -ExpandProperty Name)
        if ($productionVolumes -notcontains "rabbithole_$volumeName" -or
            $developmentVolumes -notcontains "rabbithole-dev_$volumeName" -or
            @($developmentVolumes | Where-Object { $productionVolumes -contains $_ }).Count -ne 0) {
            throw "Unexpected or shared $volumeName volume."
        }
    }
}
function Api([string]$Port, [string]$Path, [string]$Method = 'GET', $Data = $null) {
    $options = @{ Uri = "http://127.0.0.1:$Port/api$Path"; Method = $Method; TimeoutSec = 60 }
    if ($null -ne $Data) { $options.ContentType = 'application/json'; $options.Body = ConvertTo-Json $Data -Depth 10 }
    Invoke-RestMethod @options
}
if ((Api $prodPort '/healthz').status -ne 'ok' -or (Api $devPort '/healthz').status -ne 'ok') { throw 'An environment is unhealthy.' }
$before = Api $prodPort '/export' 'POST'
$productionContainers = @(Invoke-RabbitHoleCompose $prod @('ps', '-q')) | Sort-Object
$marker = 'TEST FIXTURE environment isolation ' + [Guid]::NewGuid().ToString('N')
$topic = $null
$hole = $null
try {
    $topic = Api $devPort '/topics' 'POST' @{ name = $marker; keywords = ''; weight = 0 }
    $hole = Api $devPort '/holes' 'POST' @{ title = $marker; question = 'TEST FIXTURE: does development survive a restart?'; origin = 'example' }
    $branch = Api $devPort "/holes/$($hole.id)/nodes" 'POST' @{ parentId = $hole.nodes[0].id; question = 'TEST FIXTURE: isolated branch'; origin = 'example' }
    Invoke-RabbitHoleCompose $dev @('restart')
    Invoke-RabbitHoleCompose $dev @('up', '-d', '--wait', '--no-build', '--pull', 'never')
    $topics = Api $devPort '/topics'
    $persisted = Api $devPort "/holes/$($hole.id)"
    if ($topics.id -notcontains $topic.id -or $persisted.nodes.Count -ne 2 -or $persisted.lastNodeId -ne $branch.id) {
        throw 'Development topic, branch, or position did not persist across restart.'
    }
    $after = Api $prodPort '/export' 'POST'
    if (($after | ConvertTo-Json -Depth 40) -match [regex]::Escape($marker)) { throw 'Development test data reached production.' }
    # Source refreshes may change catalog metadata while this runs. Personal state must stay exact.
    foreach ($key in @('topics', 'states', 'notes', 'documents', 'sections', 'holes', 'nodes', 'prompts', 'attempts', 'settings')) {
        $old = $before.$key | Sort-Object id, itemId, key | ConvertTo-Json -Depth 40 -Compress
        $new = $after.$key | Sort-Object id, itemId, key | ConvertTo-Json -Depth 40 -Compress
        if ($old -cne $new) { throw "Production $key changed during the development check." }
    }
    if (@($before.items | Where-Object { $after.items.id -notcontains $_.id }).Count -ne 0) { throw 'Production lost existing item IDs.' }
    $currentProductionContainers = @(Invoke-RabbitHoleCompose $prod @('ps', '-q')) | Sort-Object
    if (Compare-Object $productionContainers $currentProductionContainers) { throw 'Development restart replaced a production container.' }
    Write-Output "PASS: separate containers, networks, database/uploads volumes and loopback ports; production images pinned; development writes survive restart; production's $($after.items.Count) items and personal state preserved."
} finally {
    if ($hole) { $null = Api $devPort "/holes/$($hole.id)" 'DELETE' }
    if ($topic) { $null = Api $devPort "/topics/$($topic.id)" 'DELETE' }
}
