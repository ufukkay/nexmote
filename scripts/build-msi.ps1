param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "dev-enrollment-key",
    [string]$Version = "0.6.2"
)

$ErrorActionPreference = "Stop"
$rootDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, ".."))
$agentPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "agent")
$techPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "technician")
$cleanerPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "cleaner")
$downloadsDir = [System.IO.Path]::Combine($rootDir, "downloads")
$wixDir = [System.IO.Path]::Combine($rootDir, "artifacts", "wix")
$assetsDir = [System.IO.Path]::Combine($rootDir, "assets")
$installerAssetsDir = [System.IO.Path]::Combine($assetsDir, "installer")
$iconPath = [System.IO.Path]::Combine($assetsDir, "nexmote.ico")
$dialogBmp = [System.IO.Path]::Combine($installerAssetsDir, "dialog.bmp")
$bannerBmp = [System.IO.Path]::Combine($installerAssetsDir, "banner.bmp")
$licenseRtf = [System.IO.Path]::Combine($installerAssetsDir, "license.rtf")

function Ensure-InstallerGraphics {
    param([string]$targetDir, [string]$icoPath)
    if ((Test-Path "$targetDir\dialog.bmp") -and (Test-Path "$targetDir\banner.bmp") -and (Test-Path "$targetDir\license.rtf")) {
        return
    }

    Add-Type -AssemblyName System.Drawing
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    $icon = if (Test-Path $icoPath) { New-Object System.Drawing.Icon($icoPath, 128, 128) } else { $null }

    # 1. Dialog.bmp
    $dialogBmp = New-Object System.Drawing.Bitmap(493, 312, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g1 = [System.Drawing.Graphics]::FromImage($dialogBmp)
    $g1.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g1.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $rect1 = New-Object System.Drawing.Rectangle(0, 0, 493, 312)
    $brush1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect1, [System.Drawing.Color]::FromArgb(15, 23, 42), [System.Drawing.Color]::FromArgb(30, 58, 138), [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g1.FillRectangle($brush1, $rect1)
    $glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25, 37, 99, 235))
    $g1.FillEllipse($glowBrush, 80, 40, 320, 320)
    if ($icon -ne $null) { $g1.DrawImage($icon.ToBitmap(), 32, 45, 72, 72) }
    $fontTitle = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
    $fontSub = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Regular)
    $fontDesc = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)
    $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $cyanBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(96, 165, 250))
    $grayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(203, 213, 225))
    $g1.DrawString("NexMote", $fontTitle, $whiteBrush, 115, 45)
    $g1.DrawString("Kurumsal Uzaktan Yönetim & Destek", $fontSub, $cyanBrush, 117, 85)
    $g1.DrawString("Hızlı, güvenli ve yüksek performanslı uzaktan masaüstü`nkontrolü, donanım telemetrisi ve komut konsolu.", $fontDesc, $grayBrush, 32, 140)
    $g1.Dispose()
    $dialogBmp.Save("$targetDir\dialog.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $dialogBmp.Dispose()

    # 2. Banner.bmp
    $bannerBmp = New-Object System.Drawing.Bitmap(493, 58, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g2 = [System.Drawing.Graphics]::FromImage($bannerBmp)
    $g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
    $rect2 = New-Object System.Drawing.Rectangle(0, 0, 493, 58)
    $brush2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect2, [System.Drawing.Color]::FromArgb(15, 23, 42), [System.Drawing.Color]::FromArgb(37, 99, 235), [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal)
    $g2.FillRectangle($brush2, $rect2)
    if ($icon -ne $null) { $g2.DrawImage($icon.ToBitmap(), 445, 10, 36, 36) }
    $fontBannerTitle = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
    $fontBannerSub = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular)
    $g2.DrawString("NexMote Kurulum Sihirbazı", $fontBannerTitle, $whiteBrush, 15, 10)
    $g2.DrawString("Lütfen kurulum adımlarını takip edin.", $fontBannerSub, $grayBrush, 15, 32)
    $g2.Dispose()
    $bannerBmp.Save("$targetDir\banner.bmp", [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bannerBmp.Dispose()

    # 3. License.rtf
    $licenseRtf = "{\rtf1\ansi\ansicpg1254\deff0\nouicompat\deflang1055{\fonttbl{\f0\fnil\fcharset162 Segoe UI;}}{\colortbl ;\red15\green23\blue42;\red37\green99\blue235;\red100\green116\blue139;}\viewkind4\uc1\pard\sa200\sl276\slmult1\b\f0\fs24\cf1 NEXMOTE YAZILIM LISANS VE KULLANIM SOZLESMESI\par\b0\fs18\cf3 Surum 1.0 - Kurumsal ve Bireysel Kullanim\par\cf0\fs20\par\b 1. Lisans Hakki ve Kapsami\b0\par NexMote yazilimi, uzaktan bilgisayar yonetimi, canli masaustu destegi ve telemetri izleme amaclariyla gelistirilmistir.\par\par\b 2. Guvenlik ve Gizlilik\b0\par NexMote tum iletisim oturumlarinda TLS 1.3 ve uctan uca yetkilendirme standartlarini uygular.\par\par\b 3. Destek ve Guncellemeler\b0\par Guncellemeler ve destek icin \cf2\b https://nexmote.com\cf0\b0  adresini ziyaret edebilirsiniz.\par}"
    [System.IO.File]::WriteAllText("$targetDir\license.rtf", $licenseRtf, [System.Text.Encoding]::ASCII)
}

Ensure-InstallerGraphics -targetDir $installerAssetsDir -icoPath $iconPath

New-Item -ItemType Directory -Path $downloadsDir, $wixDir -Force | Out-Null

function Generate-AgentWxs {
    param([string]$pkgDir, [string]$outputWxs)

    $files = Get-ChildItem -Path $pkgDir -File | Where-Object { $_.Name -ne "NexMote.Agent.Windows.exe" -and $_.Name -ne "NexMote.Agent.Tray.exe" }
    
    $fileElements = ""
    $i = 0
    foreach ($f in $files) {
        $i++
        $fileElements += "      <Component Id=`"CompFile_$i`"><File Id=`"File_$i`" Source=`"$($f.FullName)`" KeyPath=`"yes`" /></Component>`n"
    }

    $wxsContent = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui"
     xmlns:util="http://wixtoolset.org/schemas/v4/wxs/util">
  <Package Name="NexMote Agent"
           Manufacturer="NexMote Inc."
           Version="$Version"
           UpgradeCode="A76F12C0-94A1-420E-B6D7-90E0F3628101"
           Scope="perMachine">

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="NexMote Agent uygulamasının daha yeni bir sürümü zaten kurulu." />
    <!-- MaximumUncompressedMediaSize açıkça büyük tutulur: paket ~380MB (üç adet self-contained/PublishSingleFile
         .NET 8 exe, her biri ~70-165MB), varsayılan WiX eşiği (200MB) aşılınca otomatik çoklu-CAB bölme devreye
         giriyor. Bu bölme, tek dosyası zaten eşiğe yakın/üzerinde olan bir paketle birleşince Windows Installer'ın
         CostFinalize/DiskCostDlg hesaplamasını bozup gerçekte 500+ GB boş alan olsa bile YANLIŞ "diskte yeterli
         alan yok" uyarısı gösteriyordu. Tek CAB'da tutmak bu hesaplama hatasını tamamen ortadan kaldırır. -->
    <MediaTemplate EmbedCab="yes" MaximumUncompressedMediaSize="1000" />

    <!-- Icon & Control Panel (Add/Remove Programs) Branding -->
    <Icon Id="NexMoteIco" SourceFile="$iconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteIco" />
    <Property Id="ARPHELPLINK" Value="https://nexmote.com" />
    <Property Id="ARPURLINFOABOUT" Value="https://nexmote.com" />
    <Property Id="ARPURLUPDATEINFO" Value="https://nexmote.com/downloads" />
    <Property Id="ARPCONTACT" Value="destek@nexmote.com" />
    <Property Id="ARPCOMMENTS" Value="NexMote Kurumsal Uzaktan Yönetim ve Canlı Destek Ajanı" />
    <Property Id="ARPNOREPAIR" Value="yes" />

    <!-- Enterprise GPO / Intune Deployment Parameters -->
    <Property Id="SERVERURL" Value="$ServerUrl" />
    <Property Id="ENROLLMENTKEY" Value="$EnrollmentKey" />
    <Property Id="LOCATIONCODE" Value="OFFICE" />

    <!-- Tek tıkla yükleyici: kurulum dizini seçme ekranı YOK (WixUI_InstallDir yerine WixUI_Minimal) —
         kullanıcıya hangi diski/klasörü seçeceği sorulmadan doğrudan ProgramFiles64Folder (C:) altına kurulur.
         Kurumsal GPO/SCCM dağıtımları hâlâ "msiexec /i ... INSTALLFOLDER=D:\..." ile geçersiz kılabilir. -->
    <ui:WixUI Id="WixUI_Minimal" />
    <WixVariable Id="WixUIDialogBmp" Value="$dialogBmp" />
    <WixVariable Id="WixUIBannerBmp" Value="$bannerBmp" />
    <WixVariable Id="WixUILicenseRtf" Value="$licenseRtf" />

    <!-- Auto-launch Tray after installation -->
    <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT" Value="NexMote Agent uygulamasını şimdi başlat" />
    <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOX" Value="1" />
    <CustomAction Id="LaunchTrayAppExecSequence"
                  Directory="INSTALLFOLDER"
                  ExeCommand="&quot;[INSTALLFOLDER]NexMote.Agent.Tray.exe&quot; --tray"
                  Execute="immediate"
                  Impersonate="yes"
                  Return="asyncNoWait" />

    <!-- Clean shutdown of processes during install/uninstall/upgrade -->
    <CustomAction Id="KillAgentTrayProcess"
                  Directory="TARGETDIR"
                  ExeCommand="cmd.exe /c &quot;taskkill /f /im NexMote.Agent.Tray.exe 2&gt;nul || exit 0&quot;"
                  Execute="immediate"
                  Return="ignore" />
    <CustomAction Id="CleanAgentProgramData"
                  Directory="TARGETDIR"
                  ExeCommand="cmd.exe /c &quot;rmdir /s /q &quot;%ProgramData%\NexMote\Agent&quot; 2&gt;nul || exit 0&quot;"
                  Execute="immediate"
                  Return="ignore" />

    <InstallExecuteSequence>
      <Custom Action="KillAgentTrayProcess" Before="InstallValidate" Condition="REMOVE=&quot;ALL&quot; or (NOT Installed and PREVIOUSVERSIONSINSTALLED)" />
      <Custom Action="CleanAgentProgramData" After="InstallFinalize" Condition="REMOVE=&quot;ALL&quot;" />
    </InstallExecuteSequence>

    <UI>
      <!-- Sade kurulum: Lisans Sözleşmesi ekranı atlanır — Welcome ekranındaki "Kur" tıklaması
           doğrudan kuruluma geçer (Order="2", kütüphanenin varsayılan WelcomeDlg->LicenseAgreementDlg
           publish'inden [Order="1"] SONRA işlenip onu ezer — WiX'in License/ReadyDlg atlama için
           standart tekniği). Lisans metni ve marka görselleri (dialog.bmp/banner.bmp) hâlâ üretiliyor
           ama artık sadece Welcome/Bitiş ekranlarında kullanılıyor. -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ProgressDlg" Order="2" Condition="1" />
      <Publish Dialog="ExitDialog" Control="Finish" Event="DoAction" Value="LaunchTrayAppExecSequence" Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
    </UI>

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteFolder" Name="NexMote">
        <Directory Id="INSTALLFOLDER" Name="Agent" />
      </Directory>
    </StandardDirectory>

    <!-- Runtime configuration read by the Windows service.
         The service prioritizes %ProgramData%\NexMote\Agent\appsettings.json,
         so the MSI must update this file during fresh installs and upgrades. -->
    <StandardDirectory Id="CommonAppDataFolder">
      <Directory Id="ProgramDataNexMoteFolder" Name="NexMote">
        <Directory Id="ProgramDataAgentFolder" Name="Agent">
          <Component Id="AgentProgramDataConfigComponent" Guid="5F0A2E35-3505-43C6-B75B-804F27E9C01A">
            <File Id="AgentProgramDataConfigFile" Source="$pkgDir\appsettings.json" Name="appsettings.json" KeyPath="yes" />
          </Component>
        </Directory>
      </Directory>
    </StandardDirectory>

    <!-- Start Menu Programs Shortcut -->
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="NexMoteProgramsFolder" Name="NexMote">
        <Component Id="AgentStartMenuComp" Guid="D8912A4F-821B-4190-84E1-912A09B2E802">
          <Shortcut Id="AgentStartMenuShortcut"
                    Name="NexMote Agent"
                    Description="NexMote Ajanı Masaüstü İstemcisi"
                    Target="[INSTALLFOLDER]NexMote.Agent.Tray.exe"
                    Arguments="--dashboard"
                    WorkingDirectory="INSTALLFOLDER"
                    Icon="NexMoteIco" />
          <RemoveFolder Id="CleanNexMoteProgramsFolder" On="uninstall" />
          <!-- HKLM, NOT HKCU: bu paket Scope="perMachine" ve bu kısayol ProgramMenuFolder'ın per-machine
               çözümlemesiyle (ortak/all-users Start Menu) tüm kullanıcılar için tek kopya olarak kuruluyor.
               KeyPath'i HKCU yapmak, Windows Installer'ın aynı transaction içinde hem "per-machine" (SYSTEM)
               hem "per-user" (impersonate edilmiş kullanıcı) bağlama geçiş yapmasını zorunlu kılan bilinen bir
               MSI anti-pattern'idir — bu da domain'e bağlı, yerel yönetici olmayan bir kullanıcıda tek kurulum
               için BİRDEN FAZLA ayrı "Windows Güvenliği" kimlik bilgisi (domain admin şifresi) isteminin
               çıkmasına yol açar. HKLM kullanmak tüm transaction'ı tutarlı biçimde tek elevasyonda tutar. -->
          <RegistryValue Root="HKLM" Key="Software\NexMote\Agent" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
        </Component>
      </Directory>
    </StandardDirectory>

    <!-- Desktop Shortcut -->
    <StandardDirectory Id="DesktopFolder">
      <Component Id="AgentDesktopShortcutComp" Guid="A9812A4F-821B-4190-84E1-912A09B2E801">
        <Shortcut Id="AgentDesktopShortcut"
                  Name="NexMote Agent"
                  Description="NexMote Ajanı Masaüstü İstemcisi"
                  Target="[INSTALLFOLDER]NexMote.Agent.Tray.exe"
                  Arguments="--dashboard"
                  WorkingDirectory="INSTALLFOLDER"
                  Icon="NexMoteIco" />
        <!-- Bkz. yukarıdaki AgentStartMenuComp notu: DesktopFolder da per-machine (ortak masaüstü) çözümleniyor,
             KeyPath HKCU değil HKLM olmalı — aksi halde çift/çoklu UAC kimlik bilgisi istemi. -->
        <RegistryValue Root="HKLM" Key="Software\NexMote\Agent" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>

    <ComponentGroup Id="AgentServiceComponents" Directory="INSTALLFOLDER">
      <Component Id="AgentServiceExeComponent">
        <File Id="AgentServiceExe" Source="$pkgDir\NexMote.Agent.Windows.exe" KeyPath="yes" />
        <ServiceInstall Id="ServiceInstaller"
                        Type="ownProcess"
                        Name="NexMote Agent"
                        DisplayName="NexMote Agent Service"
                        Description="NexMote Uzaktan Yönetim ve Destek Arka Plan Servisi."
                        Start="auto"
                        Account="LocalSystem"
                        ErrorControl="normal">
          <util:ServiceConfig FirstFailureActionType="restart"
                              SecondFailureActionType="restart"
                              ThirdFailureActionType="restart"
                              ResetPeriodInDays="1"
                              RestartServiceDelayInSeconds="5" />
        </ServiceInstall>
        <ServiceControl Id="ServiceControl"
                        Name="NexMote Agent"
                        Start="install"
                        Stop="both"
                        Remove="uninstall"
                        Wait="yes" />
      </Component>

      <Component Id="AgentTrayExeComponent">
        <File Id="AgentTrayExe" Source="$pkgDir\NexMote.Agent.Tray.exe" KeyPath="yes" />
        
        <!-- Run at Windows startup for all users -->
        <RegistryValue Root="HKLM" Key="Software\Microsoft\Windows\CurrentVersion\Run" Name="NexMoteAgentTray" Value="&quot;[INSTALLFOLDER]NexMote.Agent.Tray.exe&quot; --tray" Type="string" />
      </Component>

$fileElements
    </ComponentGroup>

    <Feature Id="AgentMainFeature" Title="NexMote Agent" Level="1">
      <ComponentGroupRef Id="AgentServiceComponents" />
      <ComponentRef Id="AgentProgramDataConfigComponent" />
      <ComponentRef Id="AgentDesktopShortcutComp" />
      <ComponentRef Id="AgentStartMenuComp" />
    </Feature>
  </Package>
</Wix>
"@

    Set-Content -Path $outputWxs -Value $wxsContent -Encoding UTF8
}

function Generate-TechnicianWxs {
    param([string]$pkgDir, [string]$outputWxs)

    $files = Get-ChildItem -Path $pkgDir -File | Where-Object { $_.Name -ne "NexMote.TechnicianApp.exe" }
    
    $fileElements = ""
    $i = 0
    foreach ($f in $files) {
        $i++
        $fileElements += "      <Component Id=`"TechCompFile_$i`"><File Id=`"TechFile_$i`" Source=`"$($f.FullName)`" KeyPath=`"yes`" /></Component>`n"
    }

    $wxsContent = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="NexMote Technician Console"
           Manufacturer="NexMote Inc."
           Version="$Version"
           UpgradeCode="B87F12C0-94A1-420E-B6D7-90E0F3628102"
           Scope="perMachine">

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="NexMote Technician Console uygulamasının daha yeni bir sürümü zaten kurulu." />
    <MediaTemplate EmbedCab="yes" MaximumUncompressedMediaSize="1000" />

    <!-- Icon & Control Panel (Add/Remove Programs) Branding -->
    <Icon Id="NexMoteTechIco" SourceFile="$iconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteTechIco" />
    <Property Id="ARPHELPLINK" Value="https://nexmote.com" />
    <Property Id="ARPURLINFOABOUT" Value="https://nexmote.com" />
    <Property Id="ARPURLUPDATEINFO" Value="https://nexmote.com/downloads" />
    <Property Id="ARPCONTACT" Value="destek@nexmote.com" />
    <Property Id="ARPCOMMENTS" Value="NexMote Uzaktan Masaüstü Yönetim ve Teknisyen Konsolu" />
    <Property Id="ARPNOREPAIR" Value="yes" />

    <!-- Tek tıkla yükleyici: kurulum dizini seçme ekranı yok, bkz. NexMote.Agent.wxs'teki aynı not. -->
    <ui:WixUI Id="WixUI_Minimal" />
    <WixVariable Id="WixUIDialogBmp" Value="$dialogBmp" />
    <WixVariable Id="WixUIBannerBmp" Value="$bannerBmp" />
    <WixVariable Id="WixUILicenseRtf" Value="$licenseRtf" />

    <!-- Auto-launch Technician Console after installation -->
    <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT" Value="NexMote Technician Console uygulamasını şimdi başlat" />
    <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOX" Value="1" />
    <CustomAction Id="LaunchTechAppExecSequence"
                  Directory="INSTALLFOLDERTECH"
                  ExeCommand="&quot;[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe&quot;"
                  Execute="immediate"
                  Impersonate="yes"
                  Return="asyncNoWait" />

    <!-- Clean shutdown of processes during install/uninstall/upgrade -->
    <CustomAction Id="KillTechProcess"
                  Directory="TARGETDIR"
                  ExeCommand="cmd.exe /c &quot;taskkill /f /im NexMote.TechnicianApp.exe 2&gt;nul || exit 0&quot;"
                  Execute="immediate"
                  Return="ignore" />

    <InstallExecuteSequence>
      <Custom Action="KillTechProcess" Before="InstallValidate" Condition="REMOVE=&quot;ALL&quot; or (NOT Installed and PREVIOUSVERSIONSINSTALLED)" />
    </InstallExecuteSequence>

    <UI>
      <!-- Sade kurulum: Lisans Sözleşmesi ekranı atlanır — bkz. NexMote.Agent.wxs'teki aynı tekniğin notu. -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ProgressDlg" Order="2" Condition="1" />
      <Publish Dialog="ExitDialog" Control="Finish" Event="DoAction" Value="LaunchTechAppExecSequence" Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
    </UI>

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteFolderTech" Name="NexMote">
        <Directory Id="INSTALLFOLDERTECH" Name="Technician" />
      </Directory>
    </StandardDirectory>

    <!-- Start Menu Programs Shortcut -->
    <StandardDirectory Id="ProgramMenuFolder">
      <Directory Id="NexMoteTechProgramsFolder" Name="NexMote">
        <Component Id="TechStartMenuComp" Guid="E8912A4F-821B-4190-84E1-912A09B2E803">
          <Shortcut Id="TechStartMenuShortcut"
                    Name="NexMote Technician Console"
                    Description="NexMote Uzaktan Destek Teknisyen Konsolu"
                    Target="[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe"
                    WorkingDirectory="INSTALLFOLDERTECH"
                    Icon="NexMoteTechIco" />
          <RemoveFolder Id="CleanNexMoteTechProgramsFolder" On="uninstall" />
          <!-- HKLM, NOT HKCU: bkz. NexMote.Agent.wxs'teki aynı düzeltmenin notu — per-machine pakette
               HKCU KeyPath, çoklu UAC kimlik bilgisi istemine yol açan bilinen bir MSI anti-pattern'idir. -->
          <RegistryValue Root="HKLM" Key="Software\NexMote\Technician" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
        </Component>
      </Directory>
    </StandardDirectory>

    <!-- Desktop Shortcut -->
    <StandardDirectory Id="DesktopFolder">
      <Component Id="DesktopShortcutComp" Guid="C9812A4F-821B-4190-84E1-912A09B2E810">
        <Shortcut Id="DesktopShortcut"
                  Name="NexMote Technician Console"
                  Description="NexMote Uzaktan Destek İstemcisi"
                  Target="[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe"
                  WorkingDirectory="INSTALLFOLDERTECH"
                  Icon="NexMoteTechIco" />
        <RegistryValue Root="HKLM" Key="Software\NexMote\Technician" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>

    <ComponentGroup Id="TechComponents" Directory="INSTALLFOLDERTECH">
      <Component Id="TechMainExeComponent">
        <File Id="TechMainExe" Source="$pkgDir\NexMote.TechnicianApp.exe" KeyPath="yes" />
        
        <!-- Protocol Handler Registration (nexmote://) -->
        <RegistryValue Root="HKLM" Key="SOFTWARE\Classes\nexmote" Value="URL:NexMote Protocol" Type="string" />
        <RegistryValue Root="HKLM" Key="SOFTWARE\Classes\nexmote" Name="URL Protocol" Value="" Type="string" />
        <RegistryValue Root="HKLM" Key="SOFTWARE\Classes\nexmote\shell\open\command" Value="&quot;[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe&quot; &quot;%1&quot;" Type="string" />

        <RegistryValue Root="HKCU" Key="Software\Classes\nexmote" Value="URL:NexMote Protocol" Type="string" />
        <RegistryValue Root="HKCU" Key="Software\Classes\nexmote" Name="URL Protocol" Value="" Type="string" />
        <RegistryValue Root="HKCU" Key="Software\Classes\nexmote\shell\open\command" Value="&quot;[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe&quot; &quot;%1&quot;" Type="string" />
      </Component>

$fileElements
    </ComponentGroup>

    <Feature Id="TechMainFeature" Title="NexMote Technician Console" Level="1">
      <ComponentGroupRef Id="TechComponents" />
      <ComponentRef Id="DesktopShortcutComp" />
      <ComponentRef Id="TechStartMenuComp" />
    </Feature>
  </Package>
</Wix>
"@

    Set-Content -Path $outputWxs -Value $wxsContent -Encoding UTF8
}

# WixToolset.UI.wixext sürümü CORE "wix" araç sürümüyle (5.0.2) BİRLİKTE pinlenir. Pin olmadan "wix build",
# bu makinede daha önce kalmış eski/uyumsuz bir sürümü (ör. proje-yerel .wix/extensions altında wixext4
# hedefli 4.0.5) sessizce kullanabiliyor — bu da WixUI diyalog tablolarının bozuk üretilmesine ve kurulum
# sırasında "Kur" butonuna basınca tekrar tekrar disk/klasör seçtirmesi gibi tuhaf davranışlara yol açıyordu.
$wixUiExtRef = "WixToolset.UI.wixext/5.0.2"
$wixUtilExtRef = "WixToolset.Util.wixext/5.0.2"

Write-Host "Generating Agent WXS..."
$agentWxs = [System.IO.Path]::Combine($wixDir, "NexMote.Agent.wxs")
Generate-AgentWxs -pkgDir $agentPkgDir -outputWxs $agentWxs

Write-Host "Building NexMote-Agent-Setup.msi..."
$agentMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Agent-Setup.msi")
wix build $agentWxs -arch x64 -ext $wixUiExtRef -ext $wixUtilExtRef -o $agentMsi

Write-Host "Generating Technician WXS..."
$techWxs = [System.IO.Path]::Combine($wixDir, "NexMote.Technician.wxs")
Generate-TechnicianWxs -pkgDir $techPkgDir -outputWxs $techWxs

Write-Host "Building NexMote-Technician-Setup.msi..."
$techMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Technician-Setup.msi")
wix build $techWxs -arch x64 -ext $wixUiExtRef -o $techMsi

function Generate-CleanerWxs {
    param([string]$pkgDir, [string]$outputWxs)

    $wxsContent = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"
     xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="NexMote Deep Cleaner"
           Manufacturer="NexMote Inc."
           Version="$Version"
           UpgradeCode="C98F12C0-94A1-420E-B6D7-90E0F3628103"
           Scope="perMachine">

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="Daha yeni bir sürüm zaten kurulu." />
    <MediaTemplate EmbedCab="yes" MaximumUncompressedMediaSize="1000" />

    <Icon Id="NexMoteCleanerIco" SourceFile="$iconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteCleanerIco" />
    <Property Id="ARPCOMMENTS" Value="NexMote Tam Kaldırıcı ve Derin Temizleyici" />
    <Property Id="ARPNOREPAIR" Value="yes" />

    <CustomAction Id="RunCleanerAction"
                  Directory="INSTALLFOLDERCLEANER"
                  ExeCommand="&quot;[INSTALLFOLDERCLEANER]NexMote.Cleaner.exe&quot; --from-msi"
                  Execute="immediate"
                  Impersonate="yes"
                  Return="asyncNoWait" />

    <InstallExecuteSequence>
      <Custom Action="RunCleanerAction" After="InstallFinalize" Condition="NOT Installed" />
    </InstallExecuteSequence>

    <UI>
      <!-- Sade kurulum: Lisans Sözleşmesi ekranı atlanır — bkz. NexMote.Agent.wxs'teki aynı tekniğin notu. -->
      <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ProgressDlg" Order="2" Condition="1" />
      <Publish Dialog="ExitDialog" Control="Finish" Event="DoAction" Value="RunCleanerAction" Condition="NOT Installed" />
    </UI>

    <ui:WixUI Id="WixUI_Minimal" />
    <WixVariable Id="WixUIDialogBmp" Value="$dialogBmp" />
    <WixVariable Id="WixUIBannerBmp" Value="$bannerBmp" />
    <WixVariable Id="WixUILicenseRtf" Value="$licenseRtf" />

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteCleanerBaseFolder" Name="NexMote">
        <Directory Id="INSTALLFOLDERCLEANER" Name="Cleaner" />
      </Directory>
    </StandardDirectory>

    <ComponentGroup Id="CleanerComponents" Directory="INSTALLFOLDERCLEANER">
      <Component Id="CleanerExeComponent">
        <File Id="CleanerExe" Source="$pkgDir\NexMote.Cleaner.exe" KeyPath="yes" />
      </Component>
    </ComponentGroup>

    <Feature Id="CleanerMainFeature" Title="NexMote Deep Cleaner" Level="1">
      <ComponentGroupRef Id="CleanerComponents" />
    </Feature>
  </Package>
</Wix>
"@

    Set-Content -Path $outputWxs -Value $wxsContent -Encoding UTF8
}

if (Test-Path $cleanerPkgDir) {
    Write-Host "Generating Cleaner WXS..."
    $cleanerWxs = [System.IO.Path]::Combine($wixDir, "NexMote.Cleaner.wxs")
    Generate-CleanerWxs -pkgDir $cleanerPkgDir -outputWxs $cleanerWxs

    Write-Host "Building NexMote-Cleanup-Setup.msi..."
    $cleanerMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Cleanup-Setup.msi")
    wix build $cleanerWxs -arch x64 -ext $wixUiExtRef -o $cleanerMsi
}

Write-Host "MSI Packages built successfully with Enterprise UI:"
Write-Host "  - $agentMsi"
Write-Host "  - $techMsi"
if (Test-Path (Join-Path $downloadsDir "NexMote-Cleanup-Setup.msi")) {
    Write-Host "  - $(Join-Path $downloadsDir 'NexMote-Cleanup-Setup.msi')"
}
