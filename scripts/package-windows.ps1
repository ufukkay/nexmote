[CmdletBinding()]
param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "",
    [string]$AdminEmail = "admin@nexmote.com",
    [string]$AdminPassword = "admin123",
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$AgentReleaseNotes,
    [Parameter(Mandatory = $true)]
    [string]$TechnicianReleaseNotes,
    [switch]$FrameworkDependent
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
$agentProject = Join-Path $root "src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"
$cleanerProject = Join-Path $root "src\NexMote.Cleaner\NexMote.Cleaner.csproj"

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
            Write-Host "Admin login not available. Using default enrollment key."
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
            Write-Host "Failed to fetch settings from $BaseUrl. Falling back to default."
        }
    }

    # Fallback to default
    return "NEXMOTE-DEMO-ENROLL-KEY-2026"
}

function Assert-UnderRoot {
    param([string]$PathToCheck)

    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    if (-not $fullPath.StartsWith($rootFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside NexMote root: $fullPath"
    }
}

Assert-UnderRoot $downloads
Assert-UnderRoot $artifacts

$EnrollmentKey = Resolve-EnrollmentKey -ExplicitKey $EnrollmentKey -BaseUrl $ServerUrl -Email $AdminEmail -Password $AdminPassword

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

Copy-Item (Join-Path $cleanerPublish "NexMote.Cleaner.exe") -Destination $agentPublish -Force

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
    & powershell -ExecutionPolicy Bypass -File $buildMsiScript -ServerUrl $ServerUrl -EnrollmentKey $EnrollmentKey -Version $Version
}

$versionsManifest = [ordered]@{
    agent = [ordered]@{
        version = $Version
        releaseNotes = $AgentReleaseNotes
    }
    technician = [ordered]@{
        version = $Version
        releaseNotes = $TechnicianReleaseNotes
    }
}
[System.IO.File]::WriteAllText((Join-Path $downloads "versions.json"), ($versionsManifest | ConvertTo-Json -Depth 4), [System.Text.Encoding]::UTF8)

Write-Host "Packaging Complete in Record Time!"
Write-Host "Wrote $(Join-Path $downloads 'versions.json') (version $Version)"
Write-Host "Agent ServerUrl: $ServerUrl"
