<#
.SYNOPSIS
    NexMote IIS Sunucusu İlk Kurulum ve Hazırlık Betiği.
    Bu betik 192.168.0.219 IIS sunucusunda Yönetici (Administrator) PowerShell ile BİR KEZ çalıştırılır.

.DESCRIPTION
    1. IIS WebSocket Protocol özelliğini etkinleştirir.
    2. C:\inetpub\wwwroot\nexmote klasörünü oluşturur ve IIS izinlerini ayarlar.
    3. IIS Application Pool (No Managed Code) ve Web Sitesi oluşturur.
    4. Klasörü sınırlı izinli ağ paylaşımına (\\192.168.0.219\nexmote) açar, böylece geliştirici bilgisayarından
       tek tıkla "deploy-iis.ps1" ile otomatik güncelleme yapılabilir.
#>

[CmdletBinding()]
param (
    [string]$SitePath = "C:\inetpub\wwwroot\nexmote",
    [string]$AppPoolName = "NexMote",
    [string]$SiteName = "NexMote",
    [int]$Port = 80,
    [int]$HttpsPort = 443,
    [string]$CertificateThumbprint = "",
    [string]$ShareName = "nexmote",
    [string]$DeploymentAccount = "Administrators"
)

Write-Host "====================================================" -ForegroundColor Cyan
Write-Host "   NexMote IIS Sunucusu Kurulum & Yapılandırma" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 1. IIS WebSockets Kontrolü & Etkinleştirme
Write-Host "`n[1/5] IIS WebSockets Protokolü kontrol ediliyor..." -ForegroundColor Yellow
try {
    $wsFeature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -ErrorAction SilentlyContinue
    if ($wsFeature -and $wsFeature.State -ne 'Enabled') {
        Write-Host "IIS-WebSockets özelliği etkinleştiriliyor..." -ForegroundColor Cyan
        Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebSockets -All -NoRestart | Out-Null
        Write-Host "IIS-WebSockets başarıyla etkinleştirildi." -ForegroundColor Green
    } else {
        Write-Host "IIS-WebSockets zaten aktif." -ForegroundColor Green
    }
} catch {
    Write-Warning "Windows Feature sorgulanamadı. Sunucu işletim sisteminde 'Web Server -> WebSocket Protocol' özelliğinin açık olduğundan emin olun."
}

# 2. Dizin Oluşturma ve İzinler
Write-Host "`n[2/5] Dizin oluşturuluyor ve NTFS izinleri atanıyor: $SitePath" -ForegroundColor Yellow
if (-not (Test-Path $SitePath)) {
    New-Item -ItemType Directory -Path $SitePath -Force | Out-Null
}
$subDirs = @("dpkeys", "logs", "wwwroot", "downloads")
foreach ($d in $subDirs) {
    $subPath = Join-Path $SitePath $d
    if (-not (Test-Path $subPath)) {
        New-Item -ItemType Directory -Path $subPath -Force | Out-Null
    }
}

# İzinler: IIS_IUSRS ve IUSR tam/değiştirme yetkisi
try {
    $acl = Get-Acl $SitePath
    $rule1 = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "Modify", "ContainerInherit,ObjectInherit", "None", "Allow")
    $rule2 = New-Object System.Security.AccessControl.FileSystemAccessRule("IUSR", "ReadAndExecute", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($rule1)
    $acl.SetAccessRule($rule2)
    Set-Acl -Path $SitePath -AclObject $acl
    Write-Host "NTFS izinleri (IIS_IUSRS / IUSR) başarıyla ayarlandı." -ForegroundColor Green
} catch {
    Write-Warning "NTFS izinleri ayarlanırken uyarı: $($_.Exception.Message)"
}

# 3. IIS WebAdministration Modülü & AppPool
Write-Host "`n[3/5] IIS Web Sitesi ve Application Pool yapılandırılıyor..." -ForegroundColor Yellow
Import-Module WebAdministration -ErrorAction SilentlyContinue

if (Get-Module -Name WebAdministration) {
    # Default Web Site Port 80 çakışmasını önlemek için durdur veya portunu değiştir
    $defaultSite = Get-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
    if ($defaultSite -and $Port -eq 80) {
        Write-Host "'Default Web Site' durduruluyor (Port 80 NexMote'a ayrılacak)..." -ForegroundColor Yellow
        Stop-Website -Name "Default Web Site" -ErrorAction SilentlyContinue
    }

    # AppPool Oluştur / Güncelle
    if (-not (Test-Path "IIS:\AppPools\$AppPoolName")) {
        New-WebAppPool -Name $AppPoolName | Out-Null
        Write-Host "Application Pool '$AppPoolName' oluşturuldu." -ForegroundColor Green
    }
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value "" # No Managed Code
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "managedPipelineMode" -Value "Integrated"
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "processModel.idleTimeout" -Value ([TimeSpan]::Zero) # Kapanmasın
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "recycling.periodicRestart.time" -Value ([TimeSpan]::Zero)

    # Web Site Oluştur / Güncelle
    $existingSite = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
    if (-not $existingSite) {
        New-Website -Name $SiteName -Port $Port -PhysicalPath $SitePath -ApplicationPool $AppPoolName | Out-Null
        Write-Host "Web Sitesi '$SiteName' (Port: $Port) oluşturuldu." -ForegroundColor Green
    } else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name "physicalPath" -Value $SitePath
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name "applicationPool" -Value $AppPoolName
        Write-Host "Web Sitesi '$SiteName' güncellendi." -ForegroundColor Green
    }

    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $HttpsPort -ErrorAction SilentlyContinue
    if (-not $httpsBinding) {
        if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
            throw "HTTPS binding bulunamadı. Production kurulumu için -CertificateThumbprint verilmelidir."
        }

        $certificatePath = "Cert:\LocalMachine\My\$CertificateThumbprint"
        if (-not (Test-Path $certificatePath)) {
            throw "HTTPS sertifikası bulunamadı: $certificatePath"
        }

        New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -IPAddress "*" -HostHeader "" | Out-Null
        $httpsBinding = Get-WebBinding -Name $SiteName -Protocol "https" -Port $HttpsPort
        $httpsBinding.AddSslCertificate($CertificateThumbprint, "My")
        Write-Host "HTTPS binding (Port: $HttpsPort) sertifika ile oluşturuldu." -ForegroundColor Green
    } else {
        Write-Host "HTTPS binding (Port: $HttpsPort) zaten mevcut." -ForegroundColor Green
    }

    Start-Website -Name $SiteName -ErrorAction SilentlyContinue
} else {
    Write-Warning "WebAdministration modülü bulunamadı. IIS Yöneticisi arayüzünden Site ve AppPool'u kontrol edebilirsiniz."
}

# 4. Ağ Paylaşımı (SMB Share) Oluşturma
Write-Host "`n[4/5] Otomatik Dağıtım için Ağ Paylaşımı oluşturuluyor: \\$env:COMPUTERNAME\$ShareName" -ForegroundColor Yellow
try {
    $existingShare = Get-SmbShare -Name $ShareName -ErrorAction SilentlyContinue
    if (-not $existingShare) {
        New-SmbShare -Name $ShareName -Path $SitePath -ChangeAccess $DeploymentAccount -Description "NexMote Deployment Share" | Out-Null
        Write-Host "Ağ paylaşımı '\\$env:COMPUTERNAME\$ShareName' oluşturuldu (${DeploymentAccount}: ChangeAccess)." -ForegroundColor Green
    } else {
        $everyoneAccess = Get-SmbShareAccess -Name $ShareName -ErrorAction SilentlyContinue |
            Where-Object { $_.AccountName -eq "Everyone" }
        if ($everyoneAccess) {
            Revoke-SmbShareAccess -Name $ShareName -AccountName "Everyone" -Force | Out-Null
            Write-Host "Ağ paylaşımındaki Everyone erişimi kaldırıldı." -ForegroundColor Green
        }

        Grant-SmbShareAccess -Name $ShareName -AccountName $DeploymentAccount -AccessRight Change -Force | Out-Null
        Write-Host "Ağ paylaşımı '$ShareName' için yalnızca '$DeploymentAccount' ChangeAccess yetkisi kullanılıyor." -ForegroundColor Green
    }
} catch {
    Write-Warning "SMB Share oluşturulurken uyarı: $($_.Exception.Message)"
}

# 5. Güvenlik Duvarı (Windows Firewall) HTTP redirect ve HTTPS portları
Write-Host "`n[5/5] Windows Firewall HTTP/HTTPS portları kontrol ediliyor..." -ForegroundColor Yellow
try {
    $rule = Get-NetFirewallRule -DisplayName "NexMote-HTTP-In" -ErrorAction SilentlyContinue
    if (-not $rule) {
        New-NetFirewallRule -DisplayName "NexMote-HTTP-In" -Direction Inbound -LocalPort $Port -Protocol TCP -Action Allow | Out-Null
        Write-Host "Firewall HTTP redirect kuralı (Port $Port TCP) eklendi." -ForegroundColor Green
    } else {
        Write-Host "Firewall HTTP redirect kuralı zaten mevcut." -ForegroundColor Green
    }

    $httpsRule = Get-NetFirewallRule -DisplayName "NexMote-HTTPS-In" -ErrorAction SilentlyContinue
    if (-not $httpsRule) {
        New-NetFirewallRule -DisplayName "NexMote-HTTPS-In" -Direction Inbound -LocalPort $HttpsPort -Protocol TCP -Action Allow | Out-Null
        Write-Host "Firewall HTTPS kuralı (Port $HttpsPort TCP) eklendi." -ForegroundColor Green
    } else {
        Write-Host "Firewall HTTPS kuralı zaten mevcut." -ForegroundColor Green
    }
} catch {
    Write-Warning "Firewall kuralı eklenemedi: $($_.Exception.Message)"
}

Write-Host "`n====================================================" -ForegroundColor Green
Write-Host "   Kurulum Tamamlandı!" -ForegroundColor Green
Write-Host "   Artık geliştirici bilgisayarınızdan şu komutla" -ForegroundColor White
Write-Host "   tek tıkla dağıtım yapabilirsiniz:" -ForegroundColor White
Write-Host "   .\scripts\deploy-iis.ps1 -RemoteShare '\\192.168.0.219\$ShareName'" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Green
