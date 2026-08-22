[CmdletBinding()]
param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "",
    [string]$AdminEmail = "admin@nexmote.com",
    [string]$AdminPassword = "admin123",
    [string]$Version = "0.6.2",
    [string]$AgentReleaseNotes = "v0.6.2: Klavye ve fare girdi yonlendirme onarimi, sadelestirilmis sag tik menusu, Denetim Masasi uzerinden kaldirma ve arka plan guncelleme iyilestirmesi.",
    [string]$TechnicianReleaseNotes = "v0.6.2: UTF-8 karakter duzeltmesi, canli ekran optimizasyonlari ve gelistirilmis uzaktan guncelleme yoneticisi.",
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
$technicianPublish = Join-Path $artifacts "technician"
$cleanerPublish = Join-Path $artifacts "cleaner"
$agentProject = Join-Path $root "src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"
$cleanerProject = Join-Path $root "src\NexMote.Cleaner\NexMote.Cleaner.csproj"
$installerAssets = Join-Path $root "scripts\installer-assets"

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
            $login = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/api/auth/login" -Method Post -ContentType "application/json" -Body $loginBody
            $adminToken = $login.token
        }
        catch {
            throw "EnrollmentKey was not provided and admin login failed. Pass -EnrollmentKey, set NEXMOTE_ENROLLMENT_KEY, or set NEXMOTE_ADMIN_API_KEY. Details: $($_.Exception.Message)"
        }
    }

    try {
        $settings = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/api/settings" -Headers @{ Authorization = "Bearer $adminToken" }
        if ([string]::IsNullOrWhiteSpace($settings.enrollmentKey)) {
            throw "Server returned an empty enrollmentKey."
        }

        Write-Host "Using current EnrollmentKey from server settings."
        return [string]$settings.enrollmentKey
    }
    catch {
        throw "Could not read current EnrollmentKey from server settings. Pass -EnrollmentKey explicitly. Details: $($_.Exception.Message)"
    }
}

function Assert-UnderRoot {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside NexMote root: $fullPath"
    }
}

Assert-UnderRoot $downloads
Assert-UnderRoot $artifacts

$EnrollmentKey = Resolve-EnrollmentKey -ExplicitKey $EnrollmentKey -BaseUrl $ServerUrl -Email $AdminEmail -Password $AdminPassword

if (Test-Path $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $downloads, $agentPublish, $technicianPublish, $cleanerPublish | Out-Null

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

& $dotnet publish $trayProject @publishArgs -o $agentPublish
if ($LASTEXITCODE -ne 0) {
    throw "Agent tray publish failed."
}

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

if (Test-Path (Join-Path $installerAssets "agent")) {
    Copy-Item -LiteralPath (Join-Path $installerAssets "agent\install-agent.ps1") -Destination $agentPublish -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $installerAssets "agent\install.bat") -Destination $agentPublish -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $installerAssets "agent\uninstall-agent.ps1") -Destination $agentPublish -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $installerAssets "agent\README.txt") -Destination $agentPublish -Force -ErrorAction SilentlyContinue
}

if (Test-Path (Join-Path $installerAssets "technician")) {
    Copy-Item -LiteralPath (Join-Path $installerAssets "technician\install-technician.ps1") -Destination $technicianPublish -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $installerAssets "technician\uninstall-technician.ps1") -Destination $technicianPublish -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $installerAssets "technician\README.txt") -Destination $technicianPublish -Force -ErrorAction SilentlyContinue
}

Write-Host "Building Fast Standalone Installers (Inno Setup)..."
$isccPaths = @(
    "ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)
$isccExe = $null
foreach ($path in $isccPaths) {
    if (Get-Command $path -ErrorAction SilentlyContinue) { $isccExe = $path; break }
    if (Test-Path $path) { $isccExe = $path; break }
}

if ($isccExe) {
    Write-Host "Using Inno Setup Compiler: $isccExe"
    Remove-Item -Path (Join-Path $downloads "NexMote-*.exe") -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 300
    $agentIss = Join-Path $PSScriptRoot "agent-setup.iss"
    $techIss = Join-Path $PSScriptRoot "technician-setup.iss"
    & $isccExe "/DMyAppVersion=$Version" "/Q" $agentIss
    & $isccExe "/DMyAppVersion=$Version" "/Q" $techIss
    Write-Host "Created Inno Setup Installers in $downloads"
}

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
