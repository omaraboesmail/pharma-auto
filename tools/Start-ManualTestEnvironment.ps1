#requires -Version 7.4
[CmdletBinding()]
param(
    [switch]$Stop,
    [switch]$Plan,
    [switch]$RestartControlUi,
    [ValidateSet('', 'ConfigureNetwork', 'CleanupNetwork', 'Saas', 'Connector', 'ControlUi')]
    [string]$Worker = '',
    [string]$DataRoot,
    [string]$PcIp,
    [int]$NetworkInterfaceIndex,
    [int]$SaasPort = 7081,
    [int]$ConnectorPort = 7443,
    [string]$PreviousNetworkCategory,
    [switch]$RemoveFirewall,
    [switch]$RestoreNetworkCategory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runtimeRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.local-runtime'))
$defaultDataRoot = [IO.Path]::GetFullPath((Join-Path $runtimeRoot 'manual-test-connector'))
$statePath = [IO.Path]::GetFullPath((Join-Path $runtimeRoot 'manual-test-environment.json'))
$firewallRuleName = 'Pharma Auto Manual Test'
$pwshPath = Join-Path $PSHOME 'pwsh.exe'
$cmdLauncherPath = Join-Path $PSScriptRoot 'Start-ManualTestEnvironment.cmd'
$progressActivity = 'Pharma Auto manual-test environment'

function Set-LauncherProgress([int]$Percent, [string]$Status) {
    Write-Progress `
        -Id 1 `
        -Activity $progressActivity `
        -Status $Status `
        -PercentComplete ([Math]::Clamp($Percent, 0, 100))
}

function Complete-LauncherProgress {
    Write-Progress -Id 1 -Activity $progressActivity -Completed
}

function Write-Status(
    [string]$Component,
    [string]$State,
    [string]$Message,
    [ConsoleColor]$Color = [ConsoleColor]::Gray) {
    $timestamp = Get-Date -Format 'HH:mm:ss'
    Write-Host "[$timestamp] [$($State.ToUpperInvariant())] $Component - $Message" `
        -ForegroundColor $Color
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-RepositoryLayout {
    $requiredPaths = @(
        (Join-Path $repoRoot 'saas-platform\src\Saas.Api\PharmaAuto.Saas.Api.csproj'),
        (Join-Path $repoRoot 'local-connector\src\Connector.LocalApi\PharmaAuto.Connector.LocalApi.csproj'),
        (Join-Path $repoRoot 'local-connector\src\Connector.ControlUi\PharmaAuto.Connector.ControlUi.csproj'),
        (Join-Path $repoRoot 'global.json')
    )
    foreach ($path in $requiredPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required manual-test file is missing: $path"
        }
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet is unavailable. Install the SDK pinned by global.json.'
    }
}

function Get-PrimaryNetwork {
    $route = Get-NetRoute -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop |
        Where-Object { $_.InterfaceIndex -gt 0 -and $_.NextHop -ne '0.0.0.0' } |
        Sort-Object @{ Expression = { $_.RouteMetric + $_.InterfaceMetric } } |
        Select-Object -First 1
    if ($null -eq $route) {
        throw 'No active IPv4 default route was found.'
    }
    $address = Get-NetIPAddress `
        -InterfaceIndex $route.InterfaceIndex `
        -AddressFamily IPv4 `
        -ErrorAction Stop |
        Where-Object { $_.IPAddress -notlike '169.254.*' } |
        Select-Object -First 1
    $profile = Get-NetConnectionProfile `
        -InterfaceIndex $route.InterfaceIndex `
        -ErrorAction Stop |
        Select-Object -First 1
    if ($null -eq $address -or $null -eq $profile) {
        throw 'The active route has no usable IPv4 address or Windows network profile.'
    }
    return [pscustomobject]@{
        InterfaceIndex = $route.InterfaceIndex
        InterfaceAlias = $profile.InterfaceAlias
        IpAddress = $address.IPAddress
        NetworkCategory = $profile.NetworkCategory.ToString()
    }
}

function Get-ManualFirewallRule {
    return @(Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue)
}

function Assert-ManualFirewallRule([object]$Rule) {
    $portFilter = $Rule | Get-NetFirewallPortFilter
    $addressFilter = $Rule | Get-NetFirewallAddressFilter
    $remoteAddresses = @($addressFilter.RemoteAddress)
    $valid = $Rule.Enabled -eq 'True' -and
        $Rule.Direction -eq 'Inbound' -and
        $Rule.Action -eq 'Allow' -and
        $Rule.Profile.ToString() -eq 'Private' -and
        $portFilter.Protocol -eq 'TCP' -and
        $portFilter.LocalPort -eq $ConnectorPort.ToString() -and
        $remoteAddresses -contains 'LocalSubnet'
    if (-not $valid) {
        throw "An existing '$firewallRuleName' rule does not match the safe Private/LocalSubnet TCP $ConnectorPort scope. Remove or rename it manually before continuing."
    }
}

function Assert-PortAvailable([int]$Port) {
    $listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $listener) {
        $process = Get-Process -Id $listener.OwningProcess -ErrorAction SilentlyContinue
        $name = if ($null -eq $process) { 'unknown process' } else { $process.ProcessName }
        throw "TCP port $Port is already in use by $name (PID $($listener.OwningProcess))."
    }
}

function Save-State([object]$State) {
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    $json = $State | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText($statePath, $json, [Text.UTF8Encoding]::new($false))
}

function New-WorkerArguments([string]$WorkerName, [string[]]$AdditionalArguments) {
    return @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-Worker', $WorkerName
    ) + $AdditionalArguments
}

function Start-ElevatedWorker([string]$WorkerName, [string[]]$AdditionalArguments) {
    $arguments = New-WorkerArguments $WorkerName $AdditionalArguments
    try {
        $process = Start-Process `
            -FilePath $pwshPath `
            -ArgumentList $arguments `
            -WorkingDirectory $repoRoot `
            -Verb RunAs `
            -WindowStyle Normal `
            -Wait `
            -PassThru
    }
    catch {
        throw "The visible administrator step was canceled or failed: $($_.Exception.Message)"
    }
    if ($process.ExitCode -ne 0) {
        throw "Administrator worker '$WorkerName' exited with code $($process.ExitCode)."
    }
}

function Start-VisibleWorker([string]$WorkerName, [string[]]$AdditionalArguments) {
    $arguments = New-WorkerArguments $WorkerName $AdditionalArguments
    return Start-Process `
        -FilePath $pwshPath `
        -ArgumentList $arguments `
        -WorkingDirectory $repoRoot `
        -WindowStyle Normal `
        -PassThru
}

function Start-ElevatedVisibleWorker(
    [string]$WorkerName,
    [string[]]$AdditionalArguments) {
    $arguments = New-WorkerArguments $WorkerName $AdditionalArguments
    try {
        return Start-Process `
            -FilePath $pwshPath `
            -ArgumentList $arguments `
            -WorkingDirectory $repoRoot `
            -Verb RunAs `
            -WindowStyle Normal `
            -PassThru
    }
    catch {
        throw "The visible administrator launch for '$WorkerName' was canceled or failed: $($_.Exception.Message)"
    }
}

function Wait-ForApplication(
    [string]$Label,
    [string]$ProcessName,
    [int]$WorkerProcessId,
    [DateTimeOffset]$NotBefore,
    [int]$ProgressStart = 88,
    [int]$ProgressEnd = 99) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    do {
        $remaining = [Math]::Max(0, ($deadline - [DateTimeOffset]::UtcNow).TotalSeconds)
        $elapsedRatio = [Math]::Clamp((60 - $remaining) / 60, 0, 1)
        $progress = $ProgressStart + [Math]::Floor(
            ($ProgressEnd - $ProgressStart) * $elapsedRatio)
        Set-LauncherProgress $progress "Waiting for $Label application window"
        if (-not (Get-Process -Id $WorkerProcessId -ErrorAction SilentlyContinue)) {
            throw "$Label elevation worker exited before the application started."
        }
        $application = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
            Where-Object {
                $_.StartTime.ToUniversalTime() -ge $NotBefore.AddSeconds(-5).UtcDateTime
            } |
            Sort-Object StartTime -Descending |
            Select-Object -First 1
        if ($null -ne $application) {
            Set-LauncherProgress $ProgressEnd "$Label application is running"
            Write-Status $Label 'Ready' "Application PID $($application.Id)." Green
            return $application
        }
        Start-Sleep -Milliseconds 750
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "$Label did not open within 60 seconds. Inspect its visible administrator window."
}

function Wait-ForHealth(
    [string]$Label,
    [uri]$Uri,
    [int]$ProcessId,
    [int]$ProgressStart = 40,
    [int]$ProgressEnd = 55,
    [switch]$SkipCertificateCheck) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        $remaining = [Math]::Max(0, ($deadline - [DateTimeOffset]::UtcNow).TotalSeconds)
        $elapsedRatio = [Math]::Clamp((90 - $remaining) / 90, 0, 1)
        $progress = $ProgressStart + [Math]::Floor(
            ($ProgressEnd - $ProgressStart) * $elapsedRatio)
        Set-LauncherProgress $progress "Waiting for $Label health at $Uri"
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
            throw "$Label exited before its health endpoint became available."
        }
        try {
            $parameters = @{
                Uri = $Uri
                Method = 'Get'
                TimeoutSec = 3
                ErrorAction = 'Stop'
            }
            if ($SkipCertificateCheck) {
                $parameters.SkipCertificateCheck = $true
            }
            $health = Invoke-RestMethod @parameters
            if ($health.status -eq 'ok') {
                Set-LauncherProgress $ProgressEnd "$Label is healthy"
                Write-Status $Label 'Ready' "$Uri" Green
                return
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "$Label did not become healthy within 90 seconds. Inspect its visible window."
}

function Invoke-DotnetWorker([string[]]$Arguments, [string]$Label) {
    Set-Location $repoRoot
    Write-Status $Label 'Starting' 'Launching dotnet process in this visible window.' Cyan
    & dotnet @Arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        Write-Status $Label 'Failed' "Exited with code $exitCode." Red
        Read-Host 'Press Enter to close this window'
    }
    else {
        Write-Status $Label 'Stopped' 'Process exited normally.' Yellow
    }
    exit $exitCode
}

function Stop-TrackedEnvironment {
    Set-LauncherProgress 5 'Loading tracked manual-test state'
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        Write-Status 'Launcher' 'Idle' 'No manual-test state file exists; nothing was stopped.' Yellow
        Complete-LauncherProgress
        return
    }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $minimumStart = [DateTimeOffset]::Parse($state.startedAtUtc).AddSeconds(-5)
    $processes = @($state.processes)
    [Array]::Reverse($processes)
    $processIndex = 0
    foreach ($entry in $processes) {
        $processIndex++
        $progress = 10 + [Math]::Floor(55 * $processIndex / [Math]::Max(1, $processes.Count))
        Set-LauncherProgress $progress "Stopping $($entry.role)"
        $process = Get-Process -Id $entry.processId -ErrorAction SilentlyContinue
        if ($null -eq $process) {
            Write-Status $entry.role 'Stopped' 'Process was already closed.' DarkGray
            continue
        }
        if ($process.StartTime.ToUniversalTime() -lt $minimumStart.UtcDateTime) {
            Write-Warning "Skipped reused PID $($entry.processId) for $($entry.role)."
            continue
        }
        Write-Status $entry.role 'Stopping' "PID $($entry.processId)" Yellow
        & taskkill.exe /PID $entry.processId /T /F | Out-Null
        Write-Status $entry.role 'Stopped' 'Process tree closed.' Green
    }

    if ($state.firewallCreatedByScript -or $state.networkCategoryChanged) {
        Set-LauncherProgress 75 'Restoring owned Windows network changes'
        Write-Status 'Network' 'Cleanup' 'Requesting visible administrator cleanup.' Cyan
        $cleanupArguments = @(
            '-NetworkInterfaceIndex', $state.networkInterfaceIndex,
            '-ConnectorPort', $state.connectorPort,
            '-PreviousNetworkCategory', $state.previousNetworkCategory
        )
        if ($state.firewallCreatedByScript) {
            $cleanupArguments += '-RemoveFirewall'
        }
        if ($state.networkCategoryChanged) {
            $cleanupArguments += '-RestoreNetworkCategory'
        }
        Start-ElevatedWorker 'CleanupNetwork' $cleanupArguments
    }

    Set-LauncherProgress 95 'Removing launcher state'
    Remove-Item -LiteralPath $statePath -Force
    Set-LauncherProgress 100 'Manual-test environment stopped'
    Write-Status 'Launcher' 'Complete' 'Processes and owned network changes were removed.' Green
    Complete-LauncherProgress
}

function Restart-TrackedControlUi {
    if (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        throw 'No running manual-test environment is tracked. Start the environment first.'
    }
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    Set-LauncherProgress 15 'Checking backend health before restarting Control UI'
    try {
        $saasHealth = Invoke-RestMethod `
            -Uri "http://127.0.0.1:$($state.saasPort)/health/live" `
            -TimeoutSec 5
        $connectorHealth = Invoke-RestMethod `
            -Uri "https://localhost:$($state.connectorPort)/health/live" `
            -SkipCertificateCheck `
            -TimeoutSec 5
    }
    catch {
        throw "The backend environment is not healthy: $($_.Exception.Message)"
    }
    if ($saasHealth.status -ne 'ok' -or $connectorHealth.status -ne 'ok') {
        throw 'The backend environment did not report healthy status.'
    }
    Write-Status 'Backends' 'Ready' 'Synthetic SaaS and Local Connector are healthy.' Green

    $minimumStart = [DateTimeOffset]::Parse($state.startedAtUtc).AddSeconds(-5)
    foreach ($entry in @($state.processes) | Where-Object role -eq 'Connector Control UI') {
        $process = Get-Process -Id $entry.processId -ErrorAction SilentlyContinue
        if ($null -ne $process -and
            $process.ProcessName -eq 'pwsh' -and
            $process.StartTime.ToUniversalTime() -ge $minimumStart.UtcDateTime) {
            Set-LauncherProgress 30 'Closing the failed Control UI worker'
            Write-Status 'Control UI' 'Stopping' "Closing worker PID $($process.Id)." Yellow
            & taskkill.exe /PID $process.Id /T /F | Out-Null
        }
    }

    Set-LauncherProgress 55 'Requesting Control UI administrator elevation'
    Write-Status 'Control UI' 'Pending' 'Approve the visible UAC request.' Yellow
    $launchTime = [DateTimeOffset]::UtcNow
    $controlUi = Start-ElevatedVisibleWorker 'ControlUi' @(
        '-DataRoot', $state.dataRoot,
        '-ConnectorPort', $state.connectorPort
    )
    $remainingProcesses = @($state.processes) |
        Where-Object role -ne 'Connector Control UI'
    $state.processes = @($remainingProcesses) + [pscustomobject]@{
        role = 'Connector Control UI'
        processId = $controlUi.Id
        startedAtUtc = $controlUi.StartTime.ToUniversalTime().ToString('O')
    }
    Save-State $state
    Write-Status 'Control UI' 'Started' "Elevated worker PID $($controlUi.Id)." Yellow
    $null = Wait-ForApplication `
        'Control UI' `
        'PharmaAuto.Connector.ControlUi' `
        $controlUi.Id `
        $launchTime `
        60 `
        99
    Set-LauncherProgress 100 'Connector Control UI restarted'
    Complete-LauncherProgress
    Write-Status 'Launcher' 'Complete' 'Control UI restarted; backends were left running.' Green
}

if ($Worker -eq 'ConfigureNetwork') {
    if (-not (Test-Administrator)) {
        throw 'ConfigureNetwork must run as administrator.'
    }
    $profile = Get-NetConnectionProfile -InterfaceIndex $NetworkInterfaceIndex -ErrorAction Stop |
        Select-Object -First 1
    if ($profile.NetworkCategory -ne 'Private') {
        Write-Status 'Network' 'Approval' 'Private LAN confirmation is required.' Yellow
        Write-Warning "Windows currently classifies '$($profile.Name)' as $($profile.NetworkCategory)."
        Write-Warning 'Private permits trusted local-network discovery and should only be used on your own LAN.'
        $confirmation = Read-Host 'Type PRIVATE to change this network profile for the manual test'
        if ($confirmation -cne 'PRIVATE') {
            throw 'Network profile change was not confirmed.'
        }
        Set-NetConnectionProfile `
            -InterfaceIndex $NetworkInterfaceIndex `
            -NetworkCategory Private
        Write-Status 'Network' 'Ready' "Changed '$($profile.Name)' to Private." Green
    }
    $rules = Get-ManualFirewallRule
    if ($rules.Count -gt 1) {
        throw "Multiple '$firewallRuleName' rules exist. Resolve them manually."
    }
    if ($rules.Count -eq 1) {
        Assert-ManualFirewallRule $rules[0]
        Write-Status 'Firewall' 'Ready' "Existing scoped TCP $ConnectorPort rule is valid." Green
    }
    else {
        New-NetFirewallRule `
            -DisplayName $firewallRuleName `
            -Direction Inbound `
            -Action Allow `
            -Protocol TCP `
            -LocalPort $ConnectorPort `
            -RemoteAddress LocalSubnet `
            -Profile Private | Out-Null
        Write-Status 'Firewall' 'Ready' "Created Private/LocalSubnet TCP $ConnectorPort rule." Green
    }
    exit 0
}

if ($Worker -eq 'CleanupNetwork') {
    if (-not (Test-Administrator)) {
        throw 'CleanupNetwork must run as administrator.'
    }
    if ($RemoveFirewall) {
        Get-ManualFirewallRule | Remove-NetFirewallRule
        Write-Status 'Firewall' 'Removed' "Deleted rule '$firewallRuleName'." Green
    }
    if ($RestoreNetworkCategory -and $PreviousNetworkCategory -in @('Public', 'Private')) {
        Set-NetConnectionProfile `
            -InterfaceIndex $NetworkInterfaceIndex `
            -NetworkCategory $PreviousNetworkCategory
        Write-Status 'Network' 'Restored' "Returned profile to $PreviousNetworkCategory." Green
    }
    exit 0
}

if ($Worker -eq 'Saas') {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Invoke-DotnetWorker @(
        'run',
        '--project', (Join-Path $repoRoot 'saas-platform\src\Saas.Api\PharmaAuto.Saas.Api.csproj'),
        '--no-launch-profile',
        '--',
        '--urls', "http://127.0.0.1:$SaasPort"
    ) 'Synthetic SaaS'
}

if ($Worker -eq 'Connector') {
    if ([string]::IsNullOrWhiteSpace($DataRoot) -or [string]::IsNullOrWhiteSpace($PcIp)) {
        throw 'Connector worker requires DataRoot and PcIp.'
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Invoke-DotnetWorker @(
        'run',
        '--project', (Join-Path $repoRoot 'local-connector\src\Connector.LocalApi\PharmaAuto.Connector.LocalApi.csproj'),
        '--no-launch-profile',
        '--',
        "--Connector:DataRoot=$DataRoot",
        "--Connector:PublicBaseUrl=https://${PcIp}:$ConnectorPort",
        "--Saas:BaseUrl=http://127.0.0.1:$SaasPort"
    ) 'Local Connector'
}

if ($Worker -eq 'ControlUi') {
    if ([string]::IsNullOrWhiteSpace($DataRoot)) {
        throw 'ControlUi worker requires DataRoot.'
    }
    $env:PHARMA_AUTO_CONNECTOR_DATA_ROOT = $DataRoot
    $env:PHARMA_AUTO_CONNECTOR_BASE_URL = "https://localhost:$ConnectorPort"
    Invoke-DotnetWorker @(
        'run',
        '--project', (Join-Path $repoRoot 'local-connector\src\Connector.ControlUi\PharmaAuto.Connector.ControlUi.csproj'),
        '--no-launch-profile'
    ) 'Connector Control UI'
}

Set-LauncherProgress 5 'Validating repository and .NET prerequisites'
Write-Status 'Launcher' 'Checking' 'Validating repository layout and .NET SDK.' Cyan
Assert-RepositoryLayout
Write-Status 'Launcher' 'Ready' "dotnet $(dotnet --version) is available." Green
if ($Stop) {
    Stop-TrackedEnvironment
    exit 0
}
if ($RestartControlUi) {
    Restart-TrackedControlUi
    exit 0
}

$DataRoot = if ([string]::IsNullOrWhiteSpace($DataRoot)) {
    $defaultDataRoot
}
else {
    [IO.Path]::GetFullPath($DataRoot)
}
if (-not $DataRoot.StartsWith($runtimeRoot.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw "DataRoot must stay inside the repository runtime directory: $runtimeRoot"
}

Set-LauncherProgress 12 'Detecting the active IPv4 network'
$network = Get-PrimaryNetwork
Write-Status 'Network' 'Detected' "$($network.IpAddress) on $($network.InterfaceAlias), category $($network.NetworkCategory)." Cyan
$existingRules = Get-ManualFirewallRule
if ($existingRules.Count -gt 1) {
    throw "Multiple '$firewallRuleName' rules exist. Resolve them manually."
}
if ($existingRules.Count -eq 1) {
    Assert-ManualFirewallRule $existingRules[0]
}
$firewallExisted = $existingRules.Count -eq 1
$networkNeedsChange = $network.NetworkCategory -ne 'Private'

Write-Host 'Pharma Auto manual-test environment' -ForegroundColor Cyan
Write-Host "  Repository: $repoRoot"
Write-Host "  LAN address: $($network.IpAddress) on $($network.InterfaceAlias)"
Write-Host "  Network:    $($network.NetworkCategory)"
Write-Host "  SaaS:       http://127.0.0.1:$SaasPort"
Write-Host "  Connector:  https://$($network.IpAddress):$ConnectorPort"
Write-Host "  Data:       $DataRoot"
Write-Host '  Genius:     read-only; no rebuild is performed automatically'

if ($Plan) {
    if ($networkNeedsChange) {
        Write-Host 'Plan: request visible UAC and explicit PRIVATE confirmation.' -ForegroundColor Yellow
    }
    if (-not $firewallExisted) {
        Write-Host "Plan: add Private/LocalSubnet firewall rule '$firewallRuleName'."
    }
    Write-Host 'Plan: start synthetic SaaS, Local Connector and Connector Control UI in visible windows.'
    Set-LauncherProgress 100 'Plan complete; no changes were made'
    Write-Status 'Launcher' 'Plan' 'No processes or network settings were changed.' Green
    Complete-LauncherProgress
    exit 0
}

$inheritedFirewallOwnership = $false
$inheritedNetworkOwnership = $false
$inheritedPreviousNetworkCategory = $network.NetworkCategory
if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    $existingState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $existingMinimumStart = [DateTimeOffset]::Parse(
        $existingState.startedAtUtc).AddSeconds(-5)
    $active = @($existingState.processes) | Where-Object {
        $candidate = Get-Process -Id $_.processId -ErrorAction SilentlyContinue
        $null -ne $candidate -and
            $candidate.ProcessName -eq 'pwsh' -and
            $candidate.StartTime.ToUniversalTime() -ge $existingMinimumStart.UtcDateTime
    }
    if ($active.Count -gt 0) {
        throw "A tracked manual-test environment is already running. Use '$cmdLauncherPath -Stop' first."
    }
    $inheritedFirewallOwnership = [bool]$existingState.firewallCreatedByScript
    $inheritedNetworkOwnership = [bool]$existingState.networkCategoryChanged
    if ($inheritedNetworkOwnership -and
        $existingState.previousNetworkCategory -in @('Public', 'Private')) {
        $inheritedPreviousNetworkCategory = $existingState.previousNetworkCategory
    }
    Write-Status `
        'Launcher' `
        'Recovered' `
        'Preserving cleanup ownership from a stale environment state.' `
        Yellow
    Remove-Item -LiteralPath $statePath -Force
}

Assert-PortAvailable $SaasPort
Assert-PortAvailable $ConnectorPort
Set-LauncherProgress 25 'Ports are available; preparing the network'
Write-Status 'Ports' 'Ready' "TCP $SaasPort and $ConnectorPort are available." Green
if ($networkNeedsChange -or -not $firewallExisted) {
    Set-LauncherProgress 30 'Waiting for visible Windows network approval'
    Write-Status 'Network' 'Pending' 'Opening the administrator confirmation window.' Yellow
    Start-ElevatedWorker 'ConfigureNetwork' @(
        '-NetworkInterfaceIndex', $network.InterfaceIndex,
        '-ConnectorPort', $ConnectorPort
    )
}

$configuredProfile = Get-NetConnectionProfile `
    -InterfaceIndex $network.InterfaceIndex `
    -ErrorAction Stop |
    Select-Object -First 1
if ($configuredProfile.NetworkCategory -ne 'Private') {
    throw 'The active network profile is still not Private.'
}
$configuredRules = Get-ManualFirewallRule
if ($configuredRules.Count -ne 1) {
    throw "The scoped '$firewallRuleName' firewall rule is unavailable."
}
Assert-ManualFirewallRule $configuredRules[0]
Set-LauncherProgress 38 'Private LAN and firewall are ready'
Write-Status 'Network' 'Ready' 'Private profile and scoped firewall rule verified.' Green

$state = [ordered]@{
    schemaVersion = '1.0'
    startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    repositoryRoot = $repoRoot
    dataRoot = $DataRoot
    pcIp = $network.IpAddress
    networkInterfaceIndex = $network.InterfaceIndex
    previousNetworkCategory = $inheritedPreviousNetworkCategory
    networkCategoryChanged = $inheritedNetworkOwnership -or $networkNeedsChange
    firewallRuleName = $firewallRuleName
    firewallCreatedByScript = $inheritedFirewallOwnership -or -not $firewallExisted
    saasPort = $SaasPort
    connectorPort = $ConnectorPort
    processes = @()
}

Set-LauncherProgress 42 'Starting synthetic SaaS'
Write-Status 'Synthetic SaaS' 'Starting' "Opening http://127.0.0.1:$SaasPort in a visible window." Cyan
$saas = Start-VisibleWorker 'Saas' @('-SaasPort', $SaasPort)
$state.processes += [ordered]@{
    role = 'Synthetic SaaS'
    processId = $saas.Id
    startedAtUtc = $saas.StartTime.ToUniversalTime().ToString('O')
}
Save-State $state
Write-Status 'Synthetic SaaS' 'Started' "PID $($saas.Id); waiting for health." Yellow
Wait-ForHealth `
    'Synthetic SaaS' `
    "http://127.0.0.1:$SaasPort/health/live" `
    $saas.Id `
    42 `
    58

Set-LauncherProgress 62 'Starting Local Connector'
Write-Status 'Local Connector' 'Starting' "Opening https://$($network.IpAddress):$ConnectorPort in a visible window." Cyan
$connector = Start-VisibleWorker 'Connector' @(
    '-DataRoot', $DataRoot,
    '-PcIp', $network.IpAddress,
    '-SaasPort', $SaasPort,
    '-ConnectorPort', $ConnectorPort
)
$state.processes += [ordered]@{
    role = 'Local Connector'
    processId = $connector.Id
    startedAtUtc = $connector.StartTime.ToUniversalTime().ToString('O')
}
Save-State $state
Write-Status 'Local Connector' 'Started' "PID $($connector.Id); waiting for health." Yellow
Wait-ForHealth `
    'Local Connector' `
    "https://localhost:$ConnectorPort/health/live" `
    $connector.Id `
    62 `
    82 `
    -SkipCertificateCheck

Set-LauncherProgress 88 'Opening Connector Control UI'
Write-Status 'Control UI' 'Pending' 'Approve the visible UAC request.' Yellow
$controlUiLaunchTime = [DateTimeOffset]::UtcNow
$controlUi = Start-ElevatedVisibleWorker 'ControlUi' @(
    '-DataRoot', $DataRoot,
    '-ConnectorPort', $ConnectorPort
)
$state.processes += [ordered]@{
    role = 'Connector Control UI'
    processId = $controlUi.Id
    startedAtUtc = $controlUi.StartTime.ToUniversalTime().ToString('O')
}
Save-State $state
Write-Status 'Control UI' 'Started' "Elevated worker PID $($controlUi.Id)." Yellow
$null = Wait-ForApplication `
    'Control UI' `
    'PharmaAuto.Connector.ControlUi' `
    $controlUi.Id `
    $controlUiLaunchTime `
    88 `
    99

Set-LauncherProgress 100 'Manual-test environment is ready'
Complete-LauncherProgress
Write-Host ''
Write-Host 'Manual-test environment is ready.' -ForegroundColor Green
Write-Host 'In the Control UI, optionally rebuild the read-only catalog, then click Create Pairing.'
Write-Host "Stop everything with: & '$cmdLauncherPath' -Stop"
