function Get-RabbitHoleCompose {
    param([Parameter(Mandatory = $true)][ValidateSet('dev', 'production')][string]$Environment)
    $repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    if ($Environment -eq 'production') {
        $folder = Join-Path $repo '.local/production'
        $file = Join-Path $folder 'compose.json'
        if (!(Test-Path -LiteralPath $file)) {
            throw 'No preserved production deployment exists in .local/production. Do not substitute the development Compose file.'
        }
        $config = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        if ($config.name -ne 'rabbithole' -or
            $config.volumes.database.name -ne 'rabbithole_database' -or !$config.volumes.database.external -or
            $config.volumes.uploads.name -ne 'rabbithole_uploads' -or !$config.volumes.uploads.external) {
            throw 'Production must retain the rabbithole project and its existing external data volumes.'
        }
        foreach ($service in $config.services.PSObject.Properties.Value) {
            if ($service.build -or $service.image -notmatch '^sha256:[a-f0-9]{64}$' -or $service.pull_policy -ne 'never') {
                throw 'Production must use preserved image IDs, no build contexts, and pull_policy: never.'
            }
        }
        return @('compose', '-p', 'rabbithole', '--project-directory', $folder, '--env-file', (Join-Path $folder '.env'), '-f', $file)
    }
    $envFile = Join-Path $repo '.env'
    if (!(Test-Path -LiteralPath $envFile)) {
        throw 'Create the development .env from .env.example first. Do not copy production credentials.'
    }
    return @('compose', '-p', 'rabbithole-dev', '--project-directory', $repo, '--env-file', $envFile, '-f', (Join-Path $repo 'compose.yaml'))
}

function Invoke-RabbitHoleCompose {
    param([string[]]$ComposeArgs, [string[]]$CommandArgs)
    & docker @ComposeArgs @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose failed: $($CommandArgs -join ' ')" }
}
