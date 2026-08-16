[CmdletBinding()]
param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7",
    [string]$Version = "0.5.4",
    [string]$AgentReleaseNotes = "Ajan ve Teknisyen için etkileşimli güncelleme onay diyalogları ve modern arayüz eklendi.",
    [string]$TechnicianReleaseNotes = "Teknisyen Konsolu Maximized pencere, KPI kartları, dinamik arama ve SaaS veri tablosu tasarımı uygulandı.",
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
$agentProject = Join-Path $root "src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"
$installerAssets = Join-Path $root "scripts\installer-assets"

function Assert-UnderRoot {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside NexMote root: $fullPath"
    }
}

Assert-UnderRoot $downloads
Assert-UnderRoot $artifacts

if (Test-Path $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $downloads, $agentPublish, $technicianPublish | Out-Null

$selfContained = -not $FrameworkDependent.IsPresent
$publishArgs = @("-c", "Release", "-r", "win-x64", "--self-contained", $selfContained.ToString().ToLowerInvariant())
if ($selfContained) {
    $publishArgs += @("/p:PublishSingleFile=true", "/p:IncludeNativeLibrariesForSelfExtract=true")
}

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

$agentZip = Join-Path $downloads "nexmote-agent-win-x64.zip"
$technicianZip = Join-Path $downloads "nexmote-technician-win-x64.zip"

Remove-Item -LiteralPath $agentZip, $technicianZip -Force -ErrorAction SilentlyContinue

Compress-Archive -Path (Join-Path $agentPublish "*") -DestinationPath $agentZip -Force
Compress-Archive -Path (Join-Path $technicianPublish "*") -DestinationPath $technicianZip -Force

Write-Host "Building Native Windows MSI Installers..."
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
$versionsManifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $downloads "versions.json") -Encoding UTF8

Write-Host "Created $agentZip"
Write-Host "Created $technicianZip"
Write-Host "Wrote $(Join-Path $downloads 'versions.json') (version $Version)"
Write-Host "Agent ServerUrl: $ServerUrl"
