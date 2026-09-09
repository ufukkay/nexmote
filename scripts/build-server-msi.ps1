[CmdletBinding()]
param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$Version = "0.8.0",
    [string]$DestinationDir = "",
    [switch]$SkipCodeSigning
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "   NexMote Server-Side MSI Build & Release Automation   " -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

# 1. Hedef dizini otomatik belirle:
#    - Eğer sunucu üzerinde doğrudan çalıştırılıyorsa C:\nexmote\downloads
#    - Eğer geliştirici makinesinden çalıştırılıyorsa \\192.168.0.219\nexmote\downloads
if ([string]::IsNullOrWhiteSpace($DestinationDir)) {
    if (Test-Path "C:\nexmote\downloads") {
        $DestinationDir = "C:\nexmote\downloads"
    } elseif (Test-Path "\\192.168.0.219\nexmote\downloads") {
        $DestinationDir = "\\192.168.0.219\nexmote\downloads"
    } else {
        $DestinationDir = Join-Path $root "downloads"
    }
}

Write-Host "Target Release Directory: $DestinationDir" -ForegroundColor Yellow

# 2. package-windows.ps1'i çağır
$packageScript = Join-Path $PSScriptRoot "package-windows.ps1"
$packageArgs = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $packageScript,
    "-ServerUrl", $ServerUrl,
    "-Version", $Version
)
if ($SkipCodeSigning) {
    $packageArgs += "-SkipCodeSigning"
}

Write-Host "Compiling and packaging MSI releases..." -ForegroundColor Green
& powershell @packageArgs
if ($LASTEXITCODE -ne 0) {
    throw "MSI package build failed!"
}

# 3. Paketleri ve versions.json'ı hedef sunucu dizinine kopyala
$localDownloads = Join-Path $root "downloads"

if ((Resolve-Path $localDownloads).Path -ne (Resolve-Path $DestinationDir).Path) {
    Write-Host "Synchronizing packages to Server Directory ($DestinationDir)..." -ForegroundColor Green
    if (-not (Test-Path $DestinationDir)) {
        New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    }

    Get-ChildItem -Path $localDownloads | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $DestinationDir -Force
        Write-Host "   -> Copied $($_.Name)" -ForegroundColor Gray
    }
}

# 4. wwwroot/downloads altına da kopyala (web sunucusundan doğrudan indirme için)
$wwwrootDownloads = ""
if ($DestinationDir -like "*\nexmote\downloads*") {
    $wwwrootDownloads = $DestinationDir.Replace("\nexmote\downloads", "\nexmote\wwwroot\downloads")
    if (Test-Path (Split-Path -Parent $wwwrootDownloads)) {
        if (-not (Test-Path $wwwrootDownloads)) {
            New-Item -ItemType Directory -Path $wwwrootDownloads -Force | Out-Null
        }
        Get-ChildItem -Path $localDownloads | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $wwwrootDownloads -Force
        }
        Write-Host "Synchronized to web public downloads: $wwwrootDownloads" -ForegroundColor Green
    }
}

Write-Host "`nRelease build and deployment successful!" -ForegroundColor Cyan
Write-Host "Version: $Version" -ForegroundColor Yellow
Write-Host "Verification endpoint: https://nexmote.com/api/updates/check" -ForegroundColor Yellow
