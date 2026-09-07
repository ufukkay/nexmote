[CmdletBinding()]
param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "",
    [string]$AdminEmail = "admin@nexmote.com",
    [string]$AdminPassword = "",
    [string]$Version = "0.7.3",
    [string]$AgentReleaseNotes = "NexMote Agent v0.7.3 güncellemesi.",
    [string]$TechnicianReleaseNotes = "NexMote Teknisyen Konsolu v0.7.3 güncellemesi.",
    [switch]$FrameworkDependent,
    [string]$SigningCertificateThumbprint = "",
    [string]$SigningCertificatePath = "",
    [string]$SigningCertificatePassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipCodeSigning
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$rootFullPath = [System.IO.Path]::GetFullPath($root)
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$downloads = Join-Path $root "downloads"
$artifacts = Join-Path $root "artifacts\package"
$agentPublish = Join-Path $artifacts "agent"
$trayPublish = Join-Path $artifacts "tray"
$technicianPublish = Join-Path $artifacts "technician"
$cleanerPublish = Join-Path $artifacts "cleaner"
$deployerPublish = Join-Path $artifacts "deployer"
$agentProject = Join-Path $root "src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"
$cleanerProject = Join-Path $root "src\NexMote.Cleaner\NexMote.Cleaner.csproj"
$deployerProject = Join-Path $root "src\NexMote.Deployer\NexMote.Deployer.csproj"
$signingScript = Join-Path $PSScriptRoot "signing.ps1"

if (-not (Test-Path -LiteralPath $signingScript)) {
    throw "Signing helper script not found: $signingScript"
}

. $signingScript

function Resolve-EnrollmentKey {
    param(
        [string]$ExplicitKey,
        [string]$BaseUrl,
        [string]$Email,
        [string]$Password
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitKey)) {
        return $ExplicitKey
    }

    if (-not [string]::IsNullOrWhiteSpace($env:NEXMOTE_ENROLLMENT_KEY)) {
        Write-Host "Using EnrollmentKey from NEXMOTE_ENROLLMENT_KEY."
        return $env:NEXMOTE_ENROLLMENT_KEY
    }

    $adminToken = $env:NEXMOTE_ADMIN_API_KEY
    if ([string]::IsNullOrWhiteSpace($adminToken)) {
        Write-Host "EnrollmentKey not provided. Fetching current key from $BaseUrl/api/settings..."
        if ([string]::IsNullOrWhiteSpace($Password)) {
            throw "EnrollmentKey not provided. Pass -EnrollmentKey, set NEXMOTE_ENROLLMENT_KEY, set NEXMOTE_ADMIN_API_KEY, or pass -AdminPassword for a one-time settings fetch."
        }

        $loginBody = @{
            email = $Email
            password = $Password
        } | ConvertTo-Json

        try {
            $loginRes = Invoke-RestMethod -Uri "$BaseUrl/api/auth/login" -Method Post -Body $loginBody -ContentType "application/json" -TimeoutSec 10
            if ($loginRes.token) {
                $adminToken = $loginRes.token
            }
        } catch {
            throw "Admin login not available and no explicit EnrollmentKey was provided."
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($adminToken)) {
        try {
            $headers = @{ "Authorization" = "Bearer $adminToken" }
            $settings = Invoke-RestMethod -Uri "$BaseUrl/api/settings" -Method Get -Headers $headers -TimeoutSec 10
            if ($settings.enrollmentKey) {
                Write-Host "Successfully fetched EnrollmentKey from server."
                return $settings.enrollmentKey
            }
        } catch {
            throw "Failed to fetch settings from $BaseUrl and no explicit EnrollmentKey was provided."
        }
    }

    throw "EnrollmentKey could not be resolved."
}

function Assert-UnderRoot {
    param([string]$PathToCheck)

    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside NexMote root: $fullPath"
    }
}

function Get-PackageMetadata {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Expected package was not produced: $Path"
    }

    $file = Get-Item -LiteralPath $Path
    return [ordered]@{
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
        sizeBytes = $file.Length
    }
}

Assert-UnderRoot $downloads
Assert-UnderRoot $artifacts

$EnrollmentKey = Resolve-EnrollmentKey -ExplicitKey $EnrollmentKey -BaseUrl $ServerUrl -Email $AdminEmail -Password $AdminPassword
if ([string]::IsNullOrWhiteSpace($EnrollmentKey) -or $EnrollmentKey -eq "dev-enrollment-key" -or $EnrollmentKey.StartsWith("CHANGE-ME")) {
    throw "Refusing to package Agent MSI with an empty, dev, or placeholder EnrollmentKey."
}

$signingCertificate = $null
if ($SkipCodeSigning.IsPresent) {
    Write-Warning "Code signing skipped by explicit -SkipCodeSigning. Do not publish these artifacts to production."
} else {
    $signingCertificate = Resolve-NexMoteSigningCertificate `
        -CertificateThumbprint $SigningCertificateThumbprint `
        -CertificatePath $SigningCertificatePath `
        -CertificatePassword $SigningCertificatePassword
}

if (Test-Path $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $downloads, $agentPublish, $trayPublish, $technicianPublish, $cleanerPublish | Out-Null

$selfContained = -not $FrameworkDependent.IsPresent
$publishArgs = @("-c", "Release", "-r", "win-x64", "--self-contained", $selfContained.ToString().ToLowerInvariant())
if ($selfContained) {
    $publishArgs += @("/p:PublishSingleFile=true", "/p:IncludeNativeLibrariesForSelfExtract=true")
}
# csproj'daki sabit <Version> yerine derlenen binary'ye HER ZAMAN bu script'in -Version
# parametresini gom: boylece calisan .exe'nin gomulu surumu WiX ProductVersion ve
# versions.json ile birebir ayni kalir (aksi halde ikisi birbirinden bagimsiz surukleniyordu).
$publishArgs += @("/p:Version=$Version", "/p:AssemblyVersion=$Version.0", "/p:FileVersion=$Version.0")

& $dotnet publish $agentProject @publishArgs -o $agentPublish
if ($LASTEXITCODE -ne 0) {
    throw "Agent publish failed."
}

& $dotnet publish $trayProject @publishArgs -o $trayPublish
if ($LASTEXITCODE -ne 0) {
    throw "Agent tray publish failed."
}

Copy-Item (Join-Path $trayPublish "*") -Destination $agentPublish -Recurse -Force

& $dotnet publish $technicianProject @publishArgs -o $technicianPublish
if ($LASTEXITCODE -ne 0) {
    throw "Technician publish failed."
}

& $dotnet publish $cleanerProject @publishArgs -o $cleanerPublish
if ($LASTEXITCODE -ne 0) {
    throw "Cleaner publish failed."
}

& $dotnet publish $deployerProject @publishArgs -o $deployerPublish
if ($LASTEXITCODE -ne 0) {
    throw "Deployer publish failed."
}

Copy-Item (Join-Path $cleanerPublish "NexMote.Cleaner.exe") -Destination $agentPublish -Force
if (Test-Path (Join-Path $deployerPublish "NexMote.Deployer.exe")) {
    Copy-Item (Join-Path $deployerPublish "NexMote.Deployer.exe") -Destination (Join-Path $downloads "NexMote-Deployer.exe") -Force
}

if ($null -ne $signingCertificate) {
    $publishedExecutables = @(
        (Join-Path $agentPublish "NexMote.Agent.Windows.exe"),
        (Join-Path $agentPublish "NexMote.Agent.Tray.exe"),
        (Join-Path $agentPublish "NexMote.Cleaner.exe"),
        (Join-Path $technicianPublish "NexMote.TechnicianApp.exe"),
        (Join-Path $cleanerPublish "NexMote.Cleaner.exe"),
        (Join-Path $deployerPublish "NexMote.Deployer.exe"),
        (Join-Path $downloads "NexMote-Deployer.exe")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    Invoke-NexMoteAuthenticodeSigning -Paths $publishedExecutables -Certificate $signingCertificate -TimestampUrl $TimestampUrl
    Assert-NexMoteAuthenticodeSignature -Paths $publishedExecutables -ExpectedThumbprint $signingCertificate.Thumbprint
}

$agentConfig = [ordered]@{
    Agent = [ordered]@{
        ServerUrl = $ServerUrl
        EnrollmentKey = $EnrollmentKey
        LocationCode = "LAB"
        HeartbeatSeconds = 20
    }
    Logging = [ordered]@{
        LogLevel = [ordered]@{
            Default = "Information"
            "Microsoft.Hosting.Lifetime" = "Information"
        }
    }
}

$agentConfig | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $agentPublish "appsettings.json") -Encoding UTF8

Write-Host "Compiling WiX MSI Installers..."
$buildMsiScript = Join-Path $PSScriptRoot "build-msi.ps1"
if (Test-Path $buildMsiScript) {
    $buildMsiArgs = @(
        "-ExecutionPolicy", "Bypass",
        "-File", $buildMsiScript,
        "-ServerUrl", $ServerUrl,
        "-EnrollmentKey", $EnrollmentKey,
        "-Version", $Version,
        "-TimestampUrl", $TimestampUrl
    )

    if ($SkipCodeSigning.IsPresent) {
        $buildMsiArgs += "-SkipCodeSigning"
    } else {
        if (-not [string]::IsNullOrWhiteSpace($SigningCertificateThumbprint)) {
            $buildMsiArgs += @("-SigningCertificateThumbprint", $SigningCertificateThumbprint)
        }

        if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePath)) {
            $buildMsiArgs += @("-SigningCertificatePath", $SigningCertificatePath)
        }

        if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePassword)) {
            $buildMsiArgs += @("-SigningCertificatePassword", $SigningCertificatePassword)
        }
    }

    & powershell @buildMsiArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MSI build failed."
    }
} else {
    throw "MSI build script not found: $buildMsiScript"
}

if ($null -ne $signingCertificate) {
    $builtPackages = @(
        (Join-Path $downloads "NexMote-Agent-Setup.msi"),
        (Join-Path $downloads "NexMote-Technician-Setup.msi"),
        (Join-Path $downloads "NexMote-Cleanup-Setup.msi"),
        (Join-Path $downloads "NexMote-Deployer-Setup.msi")
    ) | Where-Object { Test-Path -LiteralPath $_ }

    Assert-NexMoteAuthenticodeSignature -Paths $builtPackages -ExpectedThumbprint $signingCertificate.Thumbprint
}

$agentPackage = Get-PackageMetadata -Path (Join-Path $downloads "NexMote-Agent-Setup.msi")
$technicianPackage = Get-PackageMetadata -Path (Join-Path $downloads "NexMote-Technician-Setup.msi")
$deployerPackage = Get-PackageMetadata -Path (Join-Path $downloads "NexMote-Deployer-Setup.msi")

$versionsManifest = [ordered]@{
    agent = [ordered]@{
        version = $Version
        releaseNotes = $AgentReleaseNotes
        sha256 = $agentPackage.sha256
        sizeBytes = $agentPackage.sizeBytes
    }
    technician = [ordered]@{
        version = $Version
        releaseNotes = $TechnicianReleaseNotes
        sha256 = $technicianPackage.sha256
        sizeBytes = $technicianPackage.sizeBytes
    }
    deployer = [ordered]@{
        version = $Version
        releaseNotes = "Yerel agdaki bilgisayarlara uzaktan yonetici bilgileriyle toplu ajan yukleme araci."
        sha256 = $deployerPackage.sha256
        sizeBytes = $deployerPackage.sizeBytes
    }
}
[System.IO.File]::WriteAllText((Join-Path $downloads "versions.json"), ($versionsManifest | ConvertTo-Json -Depth 4), [System.Text.Encoding]::UTF8)
if ($null -ne $signingCertificate) {
    New-NexMoteDetachedManifestSignature `
        -ManifestPath (Join-Path $downloads "versions.json") `
        -SignaturePath (Join-Path $downloads "versions.json.sig") `
        -Certificate $signingCertificate `
        -KeyId $signingCertificate.Thumbprint
}

Write-Host "Packaging Complete in Record Time!"
Write-Host "Wrote $(Join-Path $downloads 'versions.json') (version $Version)"
Write-Host "Agent ServerUrl: $ServerUrl"
