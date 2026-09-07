<#
.SYNOPSIS
    NexMote ASP.NET Core Web API ve React Yönetici Konsolu Canlı / Staging Dağıtım Otomasyonu (Madde 11).

.DESCRIPTION
    1. React web ön yüzünü Vite ile derler ve wwwroot dizinine yazar.
    2. ASP.NET Core 8 Web API projesini linux-x64 için publish eder.
    3. versions.json ve downloads katalog dosyalarını senkronize eder.
    4. publish-linux.zip paketini oluşturur.
    5. İsteğe bağlı olarak hedef Linux sunucuya (SSH/SCP) yükler, systemd servisini kesintisiz yeniden başlatır.
    6. /health uç noktasını doğrulayarak yayın sağlığını teyit eder.

.EXAMPLE
    .\scripts\deploy-server.ps1 -BuildOnly
    .\scripts\deploy-server.ps1 -TargetHost "186.241.21.133" -TargetUser "root"
#>

[CmdletBinding()]
param(
    [string]$TargetHost = "186.241.21.133",
    [string]$TargetUser = "root",
    [string]$TargetDir = "/var/www/nexmote",
    [string]$ServiceName = "nexmote.service",
    [string]$HealthUrl = "https://nexmote.com/health",
    [switch]$BuildOnly,
    [switch]$SkipWebBuild,
    [switch]$SkipHealthCheck,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
$OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$root = Split-Path -Parent $PSScriptRoot
$webDir = Join-Path $root "web"
$apiProject = Join-Path $root "src\NexMote.Api\NexMote.Api.csproj"
$publishDir = Join-Path $root "artifacts\publish-linux"
$tarPath = Join-Path $root "publish-linux.tar.gz"
$downloadsDir = Join-Path $root "downloads"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "  NexMote Sunucu Dağıtım ve Yayınlama Otomasyonu" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Hedef Host:    $TargetHost"
Write-Host "Hedef Dizin:   $TargetDir"
Write-Host "Servis Adı:    $ServiceName"
Write-Host "Sağlık Adresi: $HealthUrl"
Write-Host "Build Only:    $BuildOnly"
Write-Host ""

# 1. React Web Ön Yüz Derleme
if (-not $SkipWebBuild) {
    Write-Host "[1/5] React 18 + TypeScript + Vite konsolu derleniyor..." -ForegroundColor Yellow
    Push-Location $webDir
    try {
        if (-not (Test-Path "node_modules")) {
            Write-Host "  npm ci çalıştırılıyor..."
            npm ci
        }
        npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "Web frontend derlemesi başarısız oldu!"
        }
        Write-Host "  React web konsolu başarıyla derlendi (src/NexMote.Api/wwwroot)." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "[1/5] Web derleme adımı (--SkipWebBuild) atlandı." -ForegroundColor DarkGray
}

# 2. .NET 8 Linux-x64 Publish
Write-Host "[2/5] ASP.NET Core 8 Web API (linux-x64) publish ediliyor..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish $apiProject -c Release -r linux-x64 --self-contained false -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw ".NET publish işlemi başarısız oldu!"
}
Write-Host "  .NET 8 ikilileri hazır: $publishDir" -ForegroundColor Green

# 3. İndirme Kataloğu ve Versiyon Manifest Senkronizasyonu
Write-Host "[3/5] İndirme kataloğu ve versions.json senkronize ediliyor..." -ForegroundColor Yellow
$targetDownloads = Join-Path $publishDir "downloads"
New-Item -ItemType Directory -Path $targetDownloads -Force | Out-Null

if (Test-Path (Join-Path $downloadsDir "versions.json")) {
    Copy-Item (Join-Path $downloadsDir "versions.json") $targetDownloads -Force
}
Get-ChildItem -Path $downloadsDir -Filter "*.msi" | ForEach-Object {
    Copy-Item $_.FullName $targetDownloads -Force
}
Get-ChildItem -Path $downloadsDir -Filter "*.exe" | ForEach-Object {
    Copy-Item $_.FullName $targetDownloads -Force
}
Write-Host "  İndirme kataloğu güncellendi." -ForegroundColor Green

# 4. TAR Dağıtım Paketi Oluşturma (Sadece API + Web Konsolu, ~25MB)
Write-Host "[4/5] publish-app.tar.gz dağıtım arşivi paketleniyor..." -ForegroundColor Yellow
$appTarPath = Join-Path $root "publish-app.tar.gz"
if (Test-Path $appTarPath) {
    Remove-Item -Force $appTarPath
}
tar -czf "$appTarPath" --exclude="./downloads" -C "$publishDir" .
$tarSizeMb = [math]::Round((Get-Item $appTarPath).Length / 1MB, 2)
Write-Host "  Paket hazır: $appTarPath ($tarSizeMb MB)" -ForegroundColor Green

if ($BuildOnly) {
    Write-Host ""
    Write-Host "Dağıtım paketi başarıyla hazırlandı. (--BuildOnly bayrağı nedeniyle sunucuya yükleme yapılmadı)." -ForegroundColor Green
    return
}

# 5. Sunucuya Gönderme & Servis Yeniden Başlatma
Write-Host "[5/5] Sunucuya yükleniyor ve servis güncelleniyor ($TargetHost)..." -ForegroundColor Yellow

if ($DryRun) {
    Write-Host "  [DRY-RUN] SCP ve SSH komutları simüle edildi, hiçbir değişiklik yapılmadı." -ForegroundColor Magenta
    return
}

Write-Host "  SCP ile uygulama paketi sunucuya aktarılıyor..."
scp -o ServerAliveInterval=15 -o ServerAliveCountMax=6 $appTarPath "${TargetUser}@${TargetHost}:/tmp/publish-app.tar.gz"
if ($LASTEXITCODE -ne 0) {
    throw "SCP dosya aktarımı başarısız oldu!"
}

# Sunucuya yükleme ve kurulum komutları
$remoteCommands = @"
set -e
echo '--- [Remote] Dağıtım Başlıyor ---'
mkdir -p /tmp/nexmote-deploy
tar -xzf /tmp/publish-app.tar.gz -C /tmp/nexmote-deploy

# Çalışan dizini koru, veritabanını, yedekleri ve ortam konfigürasyonunu ezme
mkdir -p $TargetDir
mkdir -p $TargetDir/backups
mkdir -p $TargetDir/dpkeys
mkdir -p $TargetDir/downloads

# Yeni ikili dosyaları taşı (nexmote.db, appsettings.Production.json ve downloads hariç)
rsync -av --exclude='nexmote.db*' --exclude='appsettings.Production.json' --exclude='backups/' --exclude='dpkeys/' --exclude='downloads/' /tmp/nexmote-deploy/ $TargetDir/

# İzinleri ayarla
chown -R www-data:www-data $TargetDir
chmod +x $TargetDir/NexMote.Api

# Servisi yeniden başlat
echo '--- [Remote] $ServiceName yeniden başlatılıyor ---'
systemctl restart $ServiceName
systemctl status $ServiceName --no-pager -n 5

# Geçici dosyaları temizle
rm -rf /tmp/nexmote-deploy /tmp/publish-app.tar.gz
echo '--- [Remote] Dağıtım Tamamlandı ---'
"@

Write-Host "  SSH ile kurulum ve servis yeniden başlatma yürütülüyor..."
ssh -o ServerAliveInterval=15 -o ServerAliveCountMax=6 "${TargetUser}@${TargetHost}" $remoteCommands
if ($LASTEXITCODE -ne 0) {
    throw "Sunucu tarafındaki dağıtım komutları hata döndürdü!"
}

# İndirilebilir manifest ve paketleri senkronize et
Write-Host "  İndirilebilir paketler senkronize ediliyor..."
scp -o ServerAliveInterval=15 -o ServerAliveCountMax=6 (Join-Path $downloadsDir "versions.json") "${TargetUser}@${TargetHost}:${TargetDir}/downloads/versions.json"
if (Test-Path (Join-Path $downloadsDir "NexMote-Agent-Setup.msi")) {
    scp -o ServerAliveInterval=15 -o ServerAliveCountMax=6 (Join-Path $downloadsDir "NexMote-Agent-Setup.msi") "${TargetUser}@${TargetHost}:${TargetDir}/downloads/NexMote-Agent-Setup.msi"
}
if (Test-Path (Join-Path $downloadsDir "NexMote-Technician-Setup.msi")) {
    scp -o ServerAliveInterval=15 -o ServerAliveCountMax=6 (Join-Path $downloadsDir "NexMote-Technician-Setup.msi") "${TargetUser}@${TargetHost}:${TargetDir}/downloads/NexMote-Technician-Setup.msi"
}
if (Test-Path (Join-Path $downloadsDir "NexMote-Deployer-Setup.msi")) {
    scp -o ServerAliveInterval=15 -o ServerAliveCountMax=6 (Join-Path $downloadsDir "NexMote-Deployer-Setup.msi") "${TargetUser}@${TargetHost}:${TargetDir}/downloads/NexMote-Deployer-Setup.msi"
}

# 6. Sağlık Kontrolü (Health Check)
if (-not $SkipHealthCheck) {
    Write-Host "Sağlık kontrolü yapılıyor: $HealthUrl..." -ForegroundColor Yellow
    $healthy = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        Start-Sleep -Seconds 2
        try {
            $resp = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 5
            if ($resp.status -eq "ok" -or $resp.product -eq "NexMote") {
                $healthy = $true
                Write-Host "Sağlık kontrolü BAŞARILI (Deneme $attempt): $($resp | ConvertTo-Json -Compress)" -ForegroundColor Green
                break
            }
        }
        catch {
            Write-Host "  Deneme ${attempt}: Sunucu yanıt vermedi ($($_.Exception.Message))" -ForegroundColor Gray
        }
    }

    if (-not $healthy) {
        Write-Host "UYARI: Sağlık kontrolü 10 deneme sonunda yanıt vermedi. Sunucu loglarını kontrol ediniz:" -ForegroundColor Red
        Write-Host "  ssh ${TargetUser}@${TargetHost} 'journalctl -u $ServiceName -n 50 --no-pager'" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "  NexMote Dağıtımı Başarıyla Tamamlandı!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
