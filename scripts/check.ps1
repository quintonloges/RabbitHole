$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:NEXT_TELEMETRY_DISABLED = '1'
    python -m unittest discover -s tests/scripts -v
    if ($LASTEXITCODE -ne 0) { throw 'Source pack tests failed.' }
    python -m unittest discover -s scripts/hosted -p 'test_*.py' -v
    if ($LASTEXITCODE -ne 0) { throw 'Hosted launch-evidence tests failed.' }
    dotnet test tests/api/RabbitHole.Api.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
    Push-Location apps/web
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
        npm run lint
        if ($LASTEXITCODE -ne 0) { throw 'Lint failed.' }
        npm run typecheck
        if ($LASTEXITCODE -ne 0) { throw 'TypeScript failed.' }
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    } finally { Pop-Location }
} finally { Pop-Location }
