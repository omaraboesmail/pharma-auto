#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\phase1-connector'),
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$connectorRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
$serviceOutput = Join-Path $resolvedOutput 'Service'
$controlOutput = Join-Path $resolvedOutput 'ControlUi'

if ($resolvedOutput -eq [IO.Path]::GetPathRoot($resolvedOutput)) {
    throw 'OutputPath cannot be a drive root.'
}

& dotnet publish (Join-Path $connectorRoot 'src\Connector.LocalApi\PharmaAuto.Connector.LocalApi.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $serviceOutput `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Connector service publish failed.' }

& dotnet publish (Join-Path $connectorRoot 'src\Connector.ControlUi\PharmaAuto.Connector.ControlUi.csproj') `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $controlOutput `
    -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Connector Control UI publish failed.' }

& dotnet list (Join-Path $connectorRoot 'PharmaAuto.Connector.slnx') package --vulnerable --include-transitive
if ($LASTEXITCODE -ne 0) { throw 'Dependency vulnerability inspection failed.' }

$files = Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse |
    Where-Object Name -ne 'manifest.json' |
    ForEach-Object {
        [ordered]@{
            path = [IO.Path]::GetRelativePath($resolvedOutput, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            length = $_.Length
        }
    }

$manifest = [ordered]@{
    schemaVersion = '1.0'
    product = 'Pharma Auto Connector'
    phase = 'PHASE_1_READ_ONLY'
    runtime = $Runtime
    createdAt = [DateTimeOffset]::UtcNow.ToString('O')
    geniusWritesEnabled = $false
    files = @($files)
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'manifest.json') -Encoding utf8NoBOM

Write-Host "Phase 1 Connector package created at $resolvedOutput" -ForegroundColor Green
Write-Host 'The package is unsigned until the release-signing pipeline applies an approved certificate.' -ForegroundColor Yellow
