#requires -Version 7.4
#requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [string]$InstallRoot = (Join-Path $env:ProgramFiles 'Pharma Auto\Connector'),
    [string]$DataRoot = (Join-Path $env:ProgramData 'PharmaAuto\Connector'),
    [string]$PharmacyDisplayName,
    [string]$PublicBaseUrl,
    [string]$SaasBaseUrl,
    [string]$TlsCertificateThumbprint,
    [string]$SaasClientCertificateThumbprint,
    [switch]$AllowUnsignedLabBuild,
    [switch]$LaunchControlUi
)

$ErrorActionPreference = 'Stop'
$serviceName = 'PharmaAutoConnector'
$serviceAccount = "NT SERVICE\$serviceName"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this visible installer from an elevated PowerShell window.'
    }
}

function Assert-SafeDirectory([string]$Path, [string]$Label) {
    $resolved = [IO.Path]::GetFullPath($Path)
    $root = [IO.Path]::GetPathRoot($resolved)
    if ($resolved -eq $root -or $resolved.Length -lt ($root.Length + 8)) {
        throw "$Label resolves to an unsafe broad directory: $resolved"
    }
    return $resolved
}

function Read-Required([string]$Prompt, [string]$Current) {
    if (-not [string]::IsNullOrWhiteSpace($Current)) { return $Current.Trim() }
    $value = Read-Host $Prompt
    if ([string]::IsNullOrWhiteSpace($value)) { throw "$Prompt is required." }
    return $value.Trim()
}

function Protect-Secret([Security.SecureString]$Secret, [string]$Name, [string]$Root) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secret)
    $plainBytes = $null
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $plainBytes = [Text.Encoding]::UTF8.GetBytes($plain)
        $entropy = [Text.Encoding]::UTF8.GetBytes("PharmaAuto.Connector.Secret.$Name.v1")
        $protected = [Security.Cryptography.ProtectedData]::Protect(
            $plainBytes,
            $entropy,
            [Security.Cryptography.DataProtectionScope]::LocalMachine)
        $secretDirectory = Join-Path $Root 'secrets'
        [IO.Directory]::CreateDirectory($secretDirectory) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $secretDirectory "$Name.dpapi"), $protected)
    }
    finally {
        if ($plainBytes) { [Security.Cryptography.CryptographicOperations]::ZeroMemory($plainBytes) }
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Grant-CertificatePrivateKeyRead(
    [Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
    [string]$Account) {
    $privateKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($Certificate)
    if ($null -eq $privateKey) {
        $privateKey = [Security.Cryptography.X509Certificates.ECDsaCertificateExtensions]::GetECDsaPrivateKey($Certificate)
    }
    if ($null -eq $privateKey) { throw "Certificate $($Certificate.Thumbprint) has no supported RSA/ECDSA private key." }
    try {
        if ($privateKey -is [Security.Cryptography.RSACng] -or
            $privateKey -is [Security.Cryptography.ECDsaCng]) {
            $keyPath = Join-Path $env:ProgramData "Microsoft\Crypto\Keys\$($privateKey.Key.UniqueName)"
        }
        elseif ($privateKey -is [Security.Cryptography.RSACryptoServiceProvider]) {
            $uniqueName = $privateKey.CspKeyContainerInfo.UniqueKeyContainerName
            $keyPath = Join-Path $env:ProgramData "Microsoft\Crypto\RSA\MachineKeys\$uniqueName"
        }
        else {
            throw "Certificate $($Certificate.Thumbprint) uses an unsupported private-key provider."
        }
        if (-not (Test-Path -LiteralPath $keyPath -PathType Leaf)) {
            throw "Certificate private-key file was not found: $keyPath"
        }
        & icacls.exe $keyPath /grant:r "${Account}:(R)" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Could not grant $Account read access to the certificate private key." }
    }
    finally {
        $privateKey.Dispose()
    }
}

Assert-Administrator
$resolvedPackage = Assert-SafeDirectory $PackagePath 'PackagePath'
$resolvedInstall = Assert-SafeDirectory $InstallRoot 'InstallRoot'
$resolvedData = Assert-SafeDirectory $DataRoot 'DataRoot'
$manifestPath = Join-Path $resolvedPackage 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'Package manifest.json is missing.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.phase -ne 'PHASE_1_READ_ONLY' -or $manifest.geniusWritesEnabled -ne $false) {
    throw 'Package is not a Phase 1 read-only Connector release.'
}
foreach ($entry in $manifest.files) {
    $file = [IO.Path]::GetFullPath((Join-Path $resolvedPackage $entry.path))
    if (-not $file.StartsWith($resolvedPackage.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest entry escapes the package: $($entry.path)"
    }
    $actual = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $entry.sha256) { throw "Package hash mismatch: $($entry.path)" }
}

$executables = Get-ChildItem -LiteralPath $resolvedPackage -Include *.exe,*.dll -File -Recurse
if (-not $AllowUnsignedLabBuild) {
    $invalid = $executables | Where-Object { (Get-AuthenticodeSignature -LiteralPath $_.FullName).Status -ne 'Valid' }
    if ($invalid) { throw 'Unsigned binaries are blocked. Use an approved signed release or explicit -AllowUnsignedLabBuild for an isolated lab.' }
}

$PharmacyDisplayName = Read-Required 'Pharmacy display name' $PharmacyDisplayName
$PublicBaseUrl = Read-Required 'Connector HTTPS LAN URL (for example https://192.168.1.20:7443)' $PublicBaseUrl
$SaasBaseUrl = Read-Required 'SaaS HTTPS URL' $SaasBaseUrl
$parsedPublicBaseUrl = $null
if (-not [Uri]::TryCreate($PublicBaseUrl, [UriKind]::Absolute, [ref]$parsedPublicBaseUrl) -or
    $parsedPublicBaseUrl.Scheme -ne 'https') { throw 'PublicBaseUrl must use HTTPS.' }
if (-not $AllowUnsignedLabBuild -and -not $SaasBaseUrl.StartsWith('https://')) { throw 'SaasBaseUrl must use HTTPS.' }

if ([string]::IsNullOrWhiteSpace($TlsCertificateThumbprint)) {
    if (-not $AllowUnsignedLabBuild) { throw 'TlsCertificateThumbprint is required for a production-style install.' }
    Write-Host 'Creating a lab-only self-signed Connector certificate…' -ForegroundColor Yellow
    $subjectAlternativeNames = @("DNS=$env:COMPUTERNAME", 'DNS=localhost')
    $parsedPublicHostAddress = $null
    if ([Net.IPAddress]::TryParse($parsedPublicBaseUrl.Host, [ref]$parsedPublicHostAddress)) {
        $subjectAlternativeNames += "IPAddress=$($parsedPublicBaseUrl.Host)"
    }
    else {
        $subjectAlternativeNames += "DNS=$($parsedPublicBaseUrl.Host)"
    }
    $certificate = New-SelfSignedCertificate `
        -Subject "CN=Pharma Auto Connector $env:COMPUTERNAME" `
        -TextExtension @("2.5.29.17={text}$($subjectAlternativeNames -join '&')") `
        -CertStoreLocation 'Cert:\LocalMachine\My' `
        -KeyAlgorithm RSA `
        -KeyLength 2048 `
        -HashAlgorithm SHA256 `
        -KeyExportPolicy NonExportable `
        -NotAfter (Get-Date).AddYears(2)
    $TlsCertificateThumbprint = $certificate.Thumbprint
}
$certificatePath = "Cert:\LocalMachine\My\$TlsCertificateThumbprint"
$certificate = Get-Item -LiteralPath $certificatePath -ErrorAction Stop
if (-not $certificate.HasPrivateKey) { throw 'Connector TLS certificate has no private key.' }
$certificateNames = @($certificate.DnsNameList | ForEach-Object Unicode)
if ($parsedPublicBaseUrl.Host -notin $certificateNames -and
    $parsedPublicBaseUrl.Host -ne $certificate.GetNameInfo(
        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
        $false)) {
    throw "Connector certificate does not cover PublicBaseUrl host $($parsedPublicBaseUrl.Host)."
}

$saasClientCertificate = $null
if (-not [string]::IsNullOrWhiteSpace($SaasClientCertificateThumbprint)) {
    $saasClientCertificate = Get-Item -LiteralPath "Cert:\LocalMachine\My\$SaasClientCertificateThumbprint" -ErrorAction Stop
    if (-not $saasClientCertificate.HasPrivateKey) { throw 'SaaS mTLS client certificate has no private key.' }
}
elseif (-not $AllowUnsignedLabBuild) {
    throw 'SaasClientCertificateThumbprint is required for a production-style install.'
}

Write-Host ''
Write-Host 'Pharma Auto Connector installation summary' -ForegroundColor Cyan
Write-Host "  Package:   $resolvedPackage"
Write-Host "  Binaries:  $resolvedInstall"
Write-Host "  Local data:$resolvedData"
Write-Host "  Pharmacy:  $PharmacyDisplayName"
Write-Host "  LAN URL:   $PublicBaseUrl"
$saasMtlsLabel = if ([string]::IsNullOrWhiteSpace($SaasClientCertificateThumbprint)) {
    'lab-only disabled'
} else {
    $SaasClientCertificateThumbprint
}
Write-Host "  SaaS mTLS: $saasMtlsLabel"
Write-Host '  ERP writes: disabled'
if (-not $PSCmdlet.ShouldProcess($resolvedInstall, 'Install the Phase 1 read-only Connector and Windows Service')) { return }

$saasSecret = Read-Host 'SaaS Connector request-signing secret (base64, stored with DPAPI)' -AsSecureString
$geniusConnection = Read-Host 'Genius SELECT-only connection string (stored with DPAPI)' -AsSecureString
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $serviceName -Force }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Existing Windows Service could not be removed.' }
    Start-Sleep -Seconds 1
}
if (Test-Path -LiteralPath $resolvedInstall) {
    Remove-Item -LiteralPath $resolvedInstall -Recurse -Force
}
[IO.Directory]::CreateDirectory($resolvedInstall) | Out-Null
[IO.Directory]::CreateDirectory($resolvedData) | Out-Null
Protect-Secret $saasSecret 'saas-request-signing-secret' $resolvedData
Protect-Secret $geniusConnection 'genius-readonly-connection' $resolvedData

$settings = [ordered]@{
    Connector = [ordered]@{
        PharmacyDisplayName = $PharmacyDisplayName
        PublicBaseUrl = $PublicBaseUrl
        TlsCertificateThumbprint = $TlsCertificateThumbprint
        Port = $parsedPublicBaseUrl.Port
    }
    Saas = [ordered]@{
        BaseUrl = $SaasBaseUrl
        ClientCertificateThumbprint = $SaasClientCertificateThumbprint
    }
    Genius = [ordered]@{ ProfileId = 'EPLUS_GENIUS_DB539_PROFILE_1' }
    Documents = [ordered]@{ TtlHours = 72; RequireDefender = $true }
}
$settings | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedData 'connector-settings.json') -Encoding utf8NoBOM

Copy-Item -Path (Join-Path $resolvedPackage 'Service\*') -Destination $resolvedInstall -Recurse -Force
$controlInstall = Join-Path $resolvedInstall 'ControlUi'
[IO.Directory]::CreateDirectory($controlInstall) | Out-Null
Copy-Item -Path (Join-Path $resolvedPackage 'ControlUi\*') -Destination $controlInstall -Recurse -Force

$serviceExe = Join-Path $resolvedInstall 'PharmaAuto.Connector.LocalApi.exe'
$binaryPath = "`"$serviceExe`" --contentRoot `"$resolvedInstall`" --Connector:DataRoot=`"$resolvedData`""
& sc.exe create $serviceName binPath= $binaryPath start= auto obj= $serviceAccount password= '' DisplayName= 'Pharma Auto Connector' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Windows Service creation failed.' }
& sc.exe description $serviceName 'Pharma Auto read-only local invoice Connector' | Out-Null
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/15000/''/0 | Out-Null

& icacls.exe $resolvedData /inheritance:r | Out-Null
& icacls.exe $resolvedData /grant:r 'SYSTEM:(OI)(CI)F' 'BUILTIN\Administrators:(OI)(CI)F' "${serviceAccount}:(OI)(CI)M" | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Local data ACL configuration failed.' }
Grant-CertificatePrivateKeyRead $certificate $serviceAccount
if ($null -ne $saasClientCertificate) {
    Grant-CertificatePrivateKeyRead $saasClientCertificate $serviceAccount
}

$firewallRuleName = 'Pharma Auto Connector (Private LAN)'
Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue |
    Remove-NetFirewallRule
New-NetFirewallRule `
    -DisplayName $firewallRuleName `
    -Direction Inbound `
    -Action Allow `
    -Protocol TCP `
    -LocalPort $parsedPublicBaseUrl.Port `
    -RemoteAddress LocalSubnet `
    -Profile Domain,Private `
    -Service $serviceName | Out-Null

$environmentPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$serviceName"
New-ItemProperty -Path $environmentPath -Name Environment -PropertyType MultiString -Value @('DOTNET_ENVIRONMENT=Production') -Force | Out-Null
New-Item -Path 'HKLM:\SOFTWARE\PharmaAuto\Connector' -Force | Out-Null
New-ItemProperty -Path 'HKLM:\SOFTWARE\PharmaAuto\Connector' -Name DataRoot -Value $resolvedData -PropertyType String -Force | Out-Null

$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonStartMenu')) 'Programs\Pharma Auto Connector.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $controlInstall 'PharmaAuto.Connector.ControlUi.exe'
$shortcut.WorkingDirectory = $controlInstall
$shortcut.Save()

Start-Service -Name $serviceName
Write-Host 'Pharma Auto Connector is installed and running. Genius writes remain disabled.' -ForegroundColor Green
if ($LaunchControlUi) { Start-Process -FilePath $shortcut.TargetPath }
