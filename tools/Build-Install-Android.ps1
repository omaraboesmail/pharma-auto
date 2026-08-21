#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$DeviceSerial,
    [string]$JavaHome,
    [string]$AndroidSdkRoot,
    [switch]$Clean,
    [switch]$Launch,
    [switch]$NoLaunch,
    [switch]$ListDevices
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$androidRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'android-client'))
$gradleWrapper = Join-Path $androidRoot 'gradlew.bat'
$apkPath = Join-Path $androidRoot 'app\build\outputs\apk\debug\app-debug.apk'
$packageName = 'com.pharmaauto.android'
$minimumApi = 28
$progressActivity = 'Build and install Pharma Auto Android'
$script:adbPath = $null

if ($Launch -and $NoLaunch) {
    throw 'Use either -Launch or -NoLaunch, not both.'
}
$shouldLaunch = -not $NoLaunch

function Set-ToolProgress([int]$Percent, [string]$Status) {
    Write-Progress `
        -Id 1 `
        -Activity $progressActivity `
        -Status $Status `
        -PercentComplete ([Math]::Clamp($Percent, 0, 100))
}

function Complete-ToolProgress {
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

function Invoke-ExternalCommand(
    [string]$FilePath,
    [string[]]$Arguments,
    [int]$TimeoutSeconds = 20,
    [switch]$AllowFailure) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FilePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Could not start $FilePath."
    }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        $process.Kill($true)
        throw "$FilePath timed out after $TimeoutSeconds seconds."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult().Trim()
    $stderr = $stderrTask.GetAwaiter().GetResult().Trim()
    $result = [pscustomobject]@{
        ExitCode = $process.ExitCode
        StdOut = $stdout
        StdErr = $stderr
    }
    $process.Dispose()
    if (-not $AllowFailure -and $result.ExitCode -ne 0) {
        $detail = @($stdout, $stderr) | Where-Object { $_ } | Join-String -Separator "`n"
        throw "$FilePath failed with exit code $($result.ExitCode).`n$detail"
    }
    return $result
}

function Invoke-Adb(
    [string[]]$Arguments,
    [int]$TimeoutSeconds = 20,
    [switch]$AllowFailure) {
    return Invoke-ExternalCommand `
        -FilePath $script:adbPath `
        -Arguments $Arguments `
        -TimeoutSeconds $TimeoutSeconds `
        -AllowFailure:$AllowFailure
}

function Resolve-AndroidSdk {
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($AndroidSdkRoot)) {
        $candidates.Add($AndroidSdkRoot)
    }
    foreach ($configured in @($env:ANDROID_SDK_ROOT, $env:ANDROID_HOME)) {
        if (-not [string]::IsNullOrWhiteSpace($configured)) {
            $candidates.Add($configured)
        }
    }
    $candidates.Add(
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\pharma-auto-android-sdk'))
    $candidates.Add((Join-Path $env:LOCALAPPDATA 'Android\Sdk'))

    $adbCommand = Get-Command adb.exe -ErrorAction SilentlyContinue
    if ($null -ne $adbCommand) {
        $candidates.Add(
            [IO.Path]::GetFullPath((Join-Path $adbCommand.Source '..\..')))
    }
    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }
        $root = [IO.Path]::GetFullPath($candidate)
        $adb = Join-Path $root 'platform-tools\adb.exe'
        if (Test-Path -LiteralPath $adb -PathType Leaf) {
            return [pscustomobject]@{ Root = $root; Adb = $adb }
        }
    }
    throw 'Android SDK platform-tools were not found. Pass -AndroidSdkRoot or set ANDROID_SDK_ROOT.'
}

function Resolve-JavaHome {
    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($JavaHome)) {
        $candidates.Add($JavaHome)
    }
    if (-not [string]::IsNullOrWhiteSpace($env:JAVA_HOME)) {
        $candidates.Add($env:JAVA_HOME)
    }
    $cachedRoot = Join-Path $env:USERPROFILE '.cache\codex-runtimes\pharma-auto-jdk17'
    if (Test-Path -LiteralPath $cachedRoot -PathType Container) {
        Get-ChildItem -LiteralPath $cachedRoot -Directory |
            Sort-Object LastWriteTime -Descending |
            ForEach-Object { $candidates.Add($_.FullName) }
    }
    $candidates.Add('C:\Program Files\Android\Android Studio\jbr')
    $javaCommand = Get-Command java.exe -ErrorAction SilentlyContinue
    if ($null -ne $javaCommand) {
        $candidates.Add(
            [IO.Path]::GetFullPath((Join-Path $javaCommand.Source '..\..')))
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }
        $root = [IO.Path]::GetFullPath($candidate)
        $java = Join-Path $root 'bin\java.exe'
        if (-not (Test-Path -LiteralPath $java -PathType Leaf)) {
            continue
        }
        $version = Invoke-ExternalCommand $java @('-version') 10 -AllowFailure
        $versionText = @($version.StdOut, $version.StdErr) -join "`n"
        $match = [regex]::Match($versionText, 'version\s+"(?<major>\d+)')
        if ($version.ExitCode -eq 0 -and $match.Success -and
            [int]$match.Groups['major'].Value -eq 17) {
            $versionLine = ($versionText -split "`r?`n" |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -First 1).Trim()
            return [pscustomobject]@{
                Root = $root
                Version = $versionLine
            }
        }
    }
    throw 'JDK 17 was not found. Pass -JavaHome or set JAVA_HOME to a JDK 17 installation.'
}

function Get-DeviceProperty([string]$Serial, [string]$Property) {
    $result = Invoke-Adb @('-s', $Serial, 'shell', 'getprop', $Property) 10 -AllowFailure
    return $result.StdOut.Trim()
}

function Get-AndroidDevices {
    $result = Invoke-Adb @('devices', '-l') 20
    $devices = [Collections.Generic.List[object]]::new()
    foreach ($line in $result.StdOut -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line) -or
            $line.StartsWith('List of devices attached')) {
            continue
        }
        $match = [regex]::Match(
            $line.Trim(),
            '^(?<serial>\S+)\s+(?<state>\S+)(?<details>.*)$')
        if (-not $match.Success) {
            continue
        }
        $serial = $match.Groups['serial'].Value
        $state = $match.Groups['state'].Value
        $details = $match.Groups['details'].Value
        $detailModel = [regex]::Match($details, '(?:^|\s)model:(?<value>\S+)')
        $model = if ($detailModel.Success) {
            $detailModel.Groups['value'].Value.Replace('_', ' ')
        }
        else {
            'Unknown model'
        }
        $manufacturer = 'Unknown manufacturer'
        $androidVersion = '?'
        $apiLevel = $null
        if ($state -eq 'device') {
            $resolvedModel = Get-DeviceProperty $serial 'ro.product.model'
            if (-not [string]::IsNullOrWhiteSpace($resolvedModel)) {
                $model = $resolvedModel
            }
            $resolvedManufacturer = Get-DeviceProperty $serial 'ro.product.manufacturer'
            if (-not [string]::IsNullOrWhiteSpace($resolvedManufacturer)) {
                $manufacturer = $resolvedManufacturer
            }
            $androidVersion = Get-DeviceProperty $serial 'ro.build.version.release'
            $apiText = Get-DeviceProperty $serial 'ro.build.version.sdk'
            if ($apiText -match '^\d+$') {
                $apiLevel = [int]$apiText
            }
        }
        $compatible = $state -eq 'device' -and
            $null -ne $apiLevel -and
            $apiLevel -ge $minimumApi
        $devices.Add([pscustomobject]@{
            Serial = $serial
            State = $state
            Manufacturer = $manufacturer
            Model = $model
            AndroidVersion = $androidVersion
            ApiLevel = $apiLevel
            Compatible = $compatible
        })
    }
    return @($devices)
}

function Show-DeviceChoices([object[]]$Devices) {
    Write-Host ''
    Write-Host 'Connected Android devices' -ForegroundColor Cyan
    for ($index = 0; $index -lt $Devices.Count; $index++) {
        $device = $Devices[$index]
        $api = if ($null -eq $device.ApiLevel) { '?' } else { $device.ApiLevel }
        $status = if ($device.State -ne 'device') {
            "UNAVAILABLE ($($device.State))"
        }
        elseif (-not $device.Compatible) {
            "INCOMPATIBLE (requires API $minimumApi+)"
        }
        else {
            'READY'
        }
        $color = if ($device.Compatible) { 'Green' } else { 'Yellow' }
        Write-Host (
            '  [{0}] {1} | {2} {3} | Android {4}, API {5} | {6}' -f
            ($index + 1),
            $device.Serial,
            $device.Manufacturer,
            $device.Model,
            $device.AndroidVersion,
            $api,
            $status
        ) -ForegroundColor $color
    }
    Write-Host ''
}

function Select-AndroidDevice([object[]]$Devices) {
    if (-not [string]::IsNullOrWhiteSpace($DeviceSerial)) {
        $selected = $Devices | Where-Object Serial -eq $DeviceSerial |
            Select-Object -First 1
        if ($null -eq $selected) {
            throw "Device '$DeviceSerial' is not present in adb devices."
        }
        if (-not $selected.Compatible) {
            throw "Device '$DeviceSerial' is not ready or is below API $minimumApi."
        }
        return $selected
    }

    if (($Devices | Where-Object Compatible).Count -eq 0) {
        throw "No connected, authorized Android device meets the API $minimumApi minimum."
    }
    while ($true) {
        $choice = Read-Host "Choose exactly one device number (1-$($Devices.Count))"
        $number = 0
        if ([int]::TryParse($choice, [ref]$number) -and
            $number -ge 1 -and
            $number -le $Devices.Count) {
            $selected = $Devices[$number - 1]
            if ($selected.Compatible) {
                return $selected
            }
            Write-Status 'Device' 'Blocked' 'That device is unavailable or incompatible.' Yellow
        }
        else {
            Write-Status 'Device' 'Invalid' 'Enter one of the displayed device numbers.' Yellow
        }
    }
}

if (-not (Test-Path -LiteralPath $gradleWrapper -PathType Leaf)) {
    throw "Gradle wrapper is missing: $gradleWrapper"
}

Set-ToolProgress 5 'Locating Android SDK and adb'
$sdk = Resolve-AndroidSdk
$script:adbPath = $sdk.Adb
Write-Status 'Android SDK' 'Ready' $sdk.Root Green

Set-ToolProgress 15 'Discovering connected Android devices'
$devices = Get-AndroidDevices
if ($devices.Count -eq 0) {
    throw 'adb reported no connected devices. Enable USB debugging and authorize this computer.'
}
Show-DeviceChoices $devices
if ($ListDevices) {
    Set-ToolProgress 100 'Device listing complete'
    Complete-ToolProgress
    exit 0
}

Set-ToolProgress 25 'Waiting for one device selection'
$selectedDevice = Select-AndroidDevice $devices
Write-Status `
    'Device' `
    'Selected' `
    "$($selectedDevice.Manufacturer) $($selectedDevice.Model), API $($selectedDevice.ApiLevel), serial $($selectedDevice.Serial)" `
    Green

Set-ToolProgress 32 'Locating and validating JDK 17'
$jdk = Resolve-JavaHome
Write-Status 'JDK' 'Ready' "$($jdk.Root) - $($jdk.Version)" Green
$env:JAVA_HOME = $jdk.Root
$env:ANDROID_SDK_ROOT = $sdk.Root
$env:ANDROID_HOME = $sdk.Root
$env:Path = "$(Join-Path $jdk.Root 'bin');$(Join-Path $sdk.Root 'platform-tools');$env:Path"

Set-ToolProgress 42 'Building the debug APK'
$tasks = [Collections.Generic.List[string]]::new()
if ($Clean) {
    $tasks.Add('clean')
}
$tasks.Add('assembleDebug')
$tasks.Add('--stacktrace')
$tasks.Add('--console=plain')
Write-Status 'Gradle' 'Building' ($tasks -join ' ') Cyan
Push-Location $androidRoot
try {
    & $gradleWrapper @tasks
    $gradleExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}
if ($gradleExitCode -ne 0) {
    Complete-ToolProgress
    throw "Gradle build failed with exit code $gradleExitCode."
}
if (-not (Test-Path -LiteralPath $apkPath -PathType Leaf)) {
    throw "Gradle succeeded but the debug APK is missing: $apkPath"
}
$apk = Get-Item -LiteralPath $apkPath
$apkSha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Status `
    'APK' `
    'Built' `
    ("{0:N1} MiB, SHA-256 {1}" -f ($apk.Length / 1MB), $apkSha256) `
    Green

Set-ToolProgress 78 'Rechecking the selected device'
$deviceState = Invoke-Adb @('-s', $selectedDevice.Serial, 'get-state') 10
if ($deviceState.StdOut -ne 'device') {
    throw "Selected device is no longer ready: $($deviceState.StdOut)"
}
$currentApi = Get-DeviceProperty $selectedDevice.Serial 'ro.build.version.sdk'
if ($currentApi -notmatch '^\d+$' -or [int]$currentApi -lt $minimumApi) {
    throw "Selected device no longer reports a compatible API level."
}

Set-ToolProgress 84 "Installing APK on $($selectedDevice.Model)"
Write-Status 'ADB' 'Installing' "Preserving app data on $($selectedDevice.Serial)." Cyan
$install = Invoke-Adb @('-s', $selectedDevice.Serial, 'install', '-r', $apkPath) 300
if ($install.StdOut -notmatch '(?m)^Success\s*$') {
    throw "adb install did not report success.`n$($install.StdOut)`n$($install.StdErr)"
}
Write-Status 'ADB' 'Installed' "$packageName on $($selectedDevice.Model)." Green

Set-ToolProgress 94 'Verifying the installed package'
$packagePath = Invoke-Adb `
    @('-s', $selectedDevice.Serial, 'shell', 'pm', 'path', $packageName) `
    20
if ($packagePath.StdOut -notmatch '^package:') {
    throw "Package verification failed: $($packagePath.StdOut)"
}
Write-Status 'Package' 'Verified' $packagePath.StdOut Green

if ($shouldLaunch) {
    Set-ToolProgress 97 'Launching Pharma Auto'
    $launchResult = Invoke-Adb @(
        '-s',
        $selectedDevice.Serial,
        'shell',
        'am',
        'start',
        '-n',
        "$packageName/.MainActivity"
    ) 20
    Write-Status 'App' 'Launched' $launchResult.StdOut Green
}

Set-ToolProgress 100 'Build, install and verification complete'
Complete-ToolProgress
Write-Host ''
Write-Host 'Pharma Auto Android is installed.' -ForegroundColor Green
Write-Host "  Device: $($selectedDevice.Manufacturer) $($selectedDevice.Model)"
Write-Host "  Serial: $($selectedDevice.Serial)"
Write-Host "  API:    $($selectedDevice.ApiLevel)"
Write-Host "  APK:    $apkPath"
Write-Host "  SHA256: $apkSha256"
Write-Host "  Launch: $shouldLaunch"
