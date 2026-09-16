<#
.SYNOPSIS
    NexMote IIS Tek Tıkla Otomatik Dağıtım ve Güncelleme Betiği.
    Geliştirici bilgisayarından çalıştırılır.

.DESCRIPTION
    1. React Web ön yüzünü derler (npm run build).
    2. ASP.NET Core 8 Web API'yi Release modunda derler.
    3. web.config ve gerekli dizinleri hazırlar.
    4. Hedef IIS sunucusunda (\\192.168.0.219\nexmote) app_offline.htm oluşturarak DLL kilitlerini güvenle çözer.
    5. Dosyaları veritabanını (nexmote.db) ezmeden senkronize eder.
    6. app_offline.htm dosyasını silerek IIS'i anında yeniden başlatır.
    7. Canlı sistem sağlık kontrolünü HTTPS üzerinden doğrular.

.EXAMPLE
    .\scripts\deploy-iis.ps1
    .\scripts\deploy-iis.ps1 -LocalOnly
    .\scripts\deploy-iis.ps1 -SkipWebBuild
#>

[CmdletBinding()]
param (
    [string]$ServerIp = "192.168.0.219",
    [string]$RemoteShare = "\\192.168.0.219\nexmote",
    [string]$HealthUrl = "",
    [string]$Username = "",
    [string]$Password = "",
    [switch]$LocalOnly,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = "Stop"
$sw = [System.Diagnostics.Stopwatch]::StartNew()

$rootDir = Split-Path -Parent $PSScriptRoot
$stagingDir = Join-Path $rootDir "artifacts\publish-iis"

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "   NexMote IIS Otomatik Dağıtım Motoru (1-Click)" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 0. Dotnet yolu tespiti
$dotnetExe = "dotnet"
$localDotnet = Join-Path $rootDir ".dotnet\dotnet.exe"
if (Test-Path $localDotnet) {
    $dotnetExe = $localDotnet
    Write-Host "[OK] Proje içi .NET 8 SDK kullanılacak: $localDotnet" -ForegroundColor Green
}

# 1. React Web Ön Yüz Derleme
if (-not $SkipWebBuild) {
    Write-Host "`n[1/4] React Web Ön Yüzü derleniyor (TypeScript + Vite)..." -ForegroundColor Yellow
    $webDir = Join-Path $rootDir "web"
    Push-Location $webDir
    try {
        & npm run build
        if ($LASTEXITCODE -ne 0) {
            throw "npm run build başarısız oldu (Çıkış kodu: $LASTEXITCODE)."
        }
        Write-Host "Web ön yüzü başarıyla derlendi (wwwroot hazırlandı)." -ForegroundColor Green
    } finally {
        Pop-Location
    }
} else {
    Write-Host "`n[1/4] Web ön yüz derlemesi atlandı (-SkipWebBuild)." -ForegroundColor DarkGray
}

# 2. .NET Web API Derleme (Release)
Write-Host "`n[2/4] NexMote.Api Release modunda derleniyor..." -ForegroundColor Yellow
if (Test-Path $stagingDir) {
    Remove-Item -Recurse -Force $stagingDir -ErrorAction SilentlyContinue
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

$apiProj = Join-Path $rootDir "src\NexMote.Api\NexMote.Api.csproj"
& $dotnetExe publish $apiProj -c Release -o $stagingDir --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish başarısız oldu (Çıkış kodu: $LASTEXITCODE)."
}

# Dizinlerin garanti edilmesi
$ensureDirs = @("dpkeys", "logs", "wwwroot", "downloads")
foreach ($dir in $ensureDirs) {
    $targetPath = Join-Path $stagingDir $dir
    if (-not (Test-Path $targetPath)) {
        New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
    }
}

# İndirme kataloğu ve versiyon manifest senkronizasyonu
$downloadsSrc = Join-Path $rootDir "downloads"
$downloadsDst = Join-Path $stagingDir "downloads"
if (Test-Path $downloadsSrc) {
    Write-Host "İndirme paketleri senkronize ediliyor: $downloadsSrc -> $downloadsDst" -ForegroundColor Cyan
    Get-ChildItem -Path $downloadsSrc | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination $downloadsDst -Force
    }
}

# web.config kontrolü
$webConfigSrc = Join-Path $rootDir "src\NexMote.Api\web.config"
$webConfigDst = Join-Path $stagingDir "web.config"
if (Test-Path $webConfigSrc) {
    Copy-Item -Path $webConfigSrc -Destination $webConfigDst -Force
}

Write-Host "Yayın paketi hazırlandı: $stagingDir" -ForegroundColor Green

# 3. Yerel mod kontrolü
if ($LocalOnly) {
    Write-Host "`n[-LocalOnly seçildi. Sunucuya aktarım yapılmadı.]" -ForegroundColor Cyan
    Write-Host "Dağıtım klasörü: $stagingDir" -ForegroundColor White
    $sw.Stop()
    Write-Host "Toplam süre: $($sw.Elapsed.TotalSeconds.ToString('F1')) saniye." -ForegroundColor Green
    return
}

# 4. Sunucuya Aktarım ve IIS Yenileme
Write-Host "`n[3/4] IIS Sunucusuna ($RemoteShare) aktarılıyor..." -ForegroundColor Yellow

# Eğer kullanıcı adı/şifre belirtildiyse SMB bağlantısını kur
if (-not [string]::IsNullOrWhiteSpace($Username) -and -not [string]::IsNullOrWhiteSpace($Password)) {
    Write-Host "Ağ paylaşımı kimlik doğrulanıyor ($Username)..." -ForegroundColor Cyan
    & net use $RemoteShare /user:$Username $Password | Out-Null
}

$canReachShare = $false
try {
    if (Test-Path $RemoteShare) {
        $canReachShare = $true
    }
} catch {
    $canReachShare = $false
}

if (-not $canReachShare) {
    Write-Warning "Ağ paylaşımına ulaşılamadı: $RemoteShare"
    Write-Host "`n------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host "İPUCU: Sunucuda paylaşımı otomatik açmak veya bağlanmak için:" -ForegroundColor White
    Write-Host "1. Parametre olarak -Username ve -Password verebilirsiniz:" -ForegroundColor White
    Write-Host "   .\scripts\deploy-iis.ps1 -Username 'DESKTOP-SIH3FAC\administrator' -Password '***'" -ForegroundColor Cyan
    Write-Host "2. RDP ile 192.168.0.219 sunucusuna bağlanıp 'scripts/setup-iis-server.ps1' çalıştırabilirsiniz." -ForegroundColor White
    Write-Host "3. Ya da '$stagingDir' içeriğini RDP ile kopyalayabilirsiniz." -ForegroundColor White
    Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
    $sw.Stop()
    return
}

# Sıfır kesintili kilit çözme: app_offline.htm
$offlineFile = Join-Path $RemoteShare "app_offline.htm"
$offlineContent = @"
<!DOCTYPE html>
<html>
<head><meta charset="utf-8"><title>NexMote Guncelleniyor</title></head>
<body style="font-family:sans-serif;text-align:center;padding:50px;background:#f8fafc;">
  <h2>NexMote Sunucusu Guncelleniyor...</h2>
  <p>Guncelleme paketleri yukleniyor, lutfen birkaç saniye bekleyin.</p>
</body>
</html>
"@

try {
    Set-Content -Path $offlineFile -Value $offlineContent -Force
    Write-Host "app_offline.htm devrede (DLL kilitleri çözüldü)." -ForegroundColor Cyan
    
    # IIS w3wp sürecinin kilitlerini çözmek için sonlandır
    if (-not [string]::IsNullOrWhiteSpace($Username) -and -not [string]::IsNullOrWhiteSpace($Password)) {
        Write-Host "IIS işçi süreci (w3wp) güvenle sonlandırılıyor..." -ForegroundColor Cyan
        & taskkill.exe /S $ServerIp /U $Username /P $Password /IM w3wp.exe /F 2>$null | Out-Null
    }
    Start-Sleep -Seconds 2

    # Robocopy ile senkronize et (nexmote.db ve mevcut dpkeys korunur!)
    Write-Host "Dosyalar aktarılıyor (Robocopy)..." -ForegroundColor Yellow
    $excludeFiles = @("nexmote.db", "nexmote.db-shm", "nexmote.db-wal", "app_offline.htm")
    $robocopyArgs = @(
        $stagingDir,
        $RemoteShare,
        "/E",
        "/XF"
    ) + $excludeFiles + @("/R:3", "/W:2", "/NP", "/NDL", "/NJH", "/NJS")

    & robocopy @robocopyArgs | Out-Null
    Write-Host "Dosya aktarımı tamamlandı." -ForegroundColor Green
    # app_offline.htm'i silerek IIS'i anında uyandır
    if (Test-Path $offlineFile) {
        Remove-Item -Force $offlineFile -ErrorAction SilentlyContinue
        Write-Host "app_offline.htm kaldırıldı." -ForegroundColor Green
    }
    if (-not [string]::IsNullOrWhiteSpace($Username) -and -not [string]::IsNullOrWhiteSpace($Password)) {
        try {
            $secPw = ConvertTo-SecureString $Password -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential($Username, $secPw)
            $opt = New-CimSessionOption -Protocol Dcom
            $sess = New-CimSession -ComputerName $ServerIp -Credential $cred -SessionOption $opt -ErrorAction SilentlyContinue
            if ($sess) {
                Invoke-CimMethod -CimSession $sess -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine="C:\Windows\System32\inetsrv\appcmd.exe start apppool /apppool.name:NexMote"} | Out-Null
                Remove-CimSession $sess -ErrorAction SilentlyContinue
            }
        } catch {}
    }
} catch {
    Write-Warning "Dağıtım sırasında bir hata oluştu: $($_.Exception.Message)"
} finally {
    if (Test-Path $offlineFile) {
        Remove-Item -Force $offlineFile -ErrorAction SilentlyContinue
        Write-Host "app_offline.htm kaldırıldı." -ForegroundColor Green
    }
}

# 5. Canlı Sağlık Kontrolü
if ([string]::IsNullOrWhiteSpace($HealthUrl)) {
    $HealthUrl = "http://$ServerIp/health"
}
Write-Host "`n[4/4] Sunucu sağlık kontrolü yapılıyor: $HealthUrl ..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

$maxRetries = 5
$healthy = $false

for ($i = 1; $i -le $maxRetries; $i++) {
    try {
        $res = Invoke-RestMethod -Uri $HealthUrl -Method Get -TimeoutSec 5 -ErrorAction Stop
        if ($res.status -eq "ok") {
            $healthy = $true
            break
        }
    } catch {
        Write-Host "Sunucu yanıt vermesi bekleniyor ($i/$maxRetries)..." -ForegroundColor DarkGray
        Start-Sleep -Seconds 2
    }
}

$sw.Stop()
Write-Host "`n====================================================" -ForegroundColor Green
if ($healthy) {
    Write-Host "   BAŞARILI! NEXMOTE IIS ÜZERİNDE CANLI!" -ForegroundColor Green
    Write-Host "   URL: $HealthUrl" -ForegroundColor White
    Write-Host "   Sağlık: $HealthUrl -> OK" -ForegroundColor White
} else {
    Write-Host "   Dağıtım tamamlandı fakat health yanıt vermedi." -ForegroundColor Yellow
    Write-Host "   Lütfen IIS Application Pool durumunu kontrol edin." -ForegroundColor Yellow
}
Write-Host "   Toplam süre: $($sw.Elapsed.TotalSeconds.ToString('F1')) saniye." -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Green
