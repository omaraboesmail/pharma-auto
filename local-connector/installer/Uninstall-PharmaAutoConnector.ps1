#requires -Version 7.4
#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'Pharma Auto\Connector'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'PharmaAuto\Connector'),
    [switch]$RemoveLocalData
)

$ErrorActionPreference = 'Stop'
$serviceName = 'PharmaAutoConnector'
$resolvedInstall = [IO.Path]::GetFullPath($InstallRoot)
$resolvedData = [IO.Path]::GetFullPath($DataRoot)

foreach ($path in @($resolvedInstall, $resolvedData)) {
    $root = [IO.Path]::GetPathRoot($path)
    if ($path -eq $root -or $path.Length -lt ($root.Length + 8)) {
        throw "Refusing unsafe uninstall target: $path"
    }
}

if (-not $PSCmdlet.ShouldProcess($resolvedInstall, 'Stop and remove the Pharma Auto Connector service and binaries')) { return }
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
}
Get-NetFirewallRule -DisplayName 'Pharma Auto Connector (Private LAN)' -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs\Pharma Auto Connector.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
if (Test-Path -LiteralPath $resolvedInstall) { Remove-Item -LiteralPath $resolvedInstall -Recurse -Force }
Remove-Item -LiteralPath 'HKLM:\SOFTWARE\PharmaAuto\Connector' -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveLocalData) {
    if ($PSCmdlet.ShouldProcess($resolvedData, 'Permanently remove encrypted documents, Sidecar, device identities and audit data')) {
        if (Test-Path -LiteralPath $resolvedData) { Remove-Item -LiteralPath $resolvedData -Recurse -Force }
        Write-Host 'Local Connector data was permanently removed and is not recoverable without a backup.' -ForegroundColor Yellow
    }
}
else {
    Write-Host "Connector binaries were removed. Encrypted local data was preserved at $resolvedData" -ForegroundColor Green
}
