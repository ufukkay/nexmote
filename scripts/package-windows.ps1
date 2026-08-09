[CmdletBinding()]
param(
    [string]$ServerUrl = "http://127.0.0.1:5080",
    [string]$EnrollmentKey = "dev-enrollment-key",
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
$agentProject = Join-Path $root "agent-windows\src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "agent-windows\src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "technician-app\src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"

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
        AgentVersion = "0.1.0"
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

Copy-Item -LiteralPath (Join-Path $root "installer\agent\install-agent.ps1") -Destination $agentPublish -Force
Copy-Item -LiteralPath (Join-Path $root "installer\agent\uninstall-agent.ps1") -Destination $agentPublish -Force
Copy-Item -LiteralPath (Join-Path $root "installer\agent\README.txt") -Destination $agentPublish -Force

Copy-Item -LiteralPath (Join-Path $root "installer\technician\install-technician.ps1") -Destination $technicianPublish -Force
Copy-Item -LiteralPath (Join-Path $root "installer\technician\uninstall-technician.ps1") -Destination $technicianPublish -Force
Copy-Item -LiteralPath (Join-Path $root "installer\technician\README.txt") -Destination $technicianPublish -Force

$agentZip = Join-Path $downloads "nexmote-agent-win-x64.zip"
$technicianZip = Join-Path $downloads "nexmote-technician-win-x64.zip"

Remove-Item -LiteralPath $agentZip, $technicianZip -Force -ErrorAction SilentlyContinue

Compress-Archive -Path (Join-Path $agentPublish "*") -DestinationPath $agentZip -Force
Compress-Archive -Path (Join-Path $technicianPublish "*") -DestinationPath $technicianZip -Force

Write-Host "Created $agentZip"
Write-Host "Created $technicianZip"
Write-Host "Agent ServerUrl: $ServerUrl"
