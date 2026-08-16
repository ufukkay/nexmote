param(
    [string]$ServerUrl = "https://nexmote.com",
    [string]$EnrollmentKey = "dev-enrollment-key",
    [string]$Version = "0.5.2"
)

$ErrorActionPreference = "Stop"
$rootDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, ".."))
$agentPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "agent")
$techPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "technician")
$downloadsDir = [System.IO.Path]::Combine($rootDir, "downloads")
$wixDir = [System.IO.Path]::Combine($rootDir, "artifacts", "wix")
$assetsDir = [System.IO.Path]::Combine($rootDir, "assets")
$installerAssetsDir = [System.IO.Path]::Combine($assetsDir, "installer")
$iconPath = [System.IO.Path]::Combine($assetsDir, "nexmote.ico")
$dialogBmp = [System.IO.Path]::Combine($installerAssetsDir, "dialog.bmp")
$bannerBmp = [System.IO.Path]::Combine($installerAssetsDir, "banner.bmp")
$licenseRtf = [System.IO.Path]::Combine($installerAssetsDir, "license.rtf")

# Ensure assets exist
if (-not (Test-Path $dialogBmp) -or -not (Test-Path $bannerBmp) -or -not (Test-Path $licenseRtf)) {
    & "$PSScriptRoot\generate-installer-graphics.ps1"
}

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
    <MediaTemplate EmbedCab="yes" />

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

    <!-- Modern WiX UI Wizard -->
    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDER" />
    <WixVariable Id="WixUIDialogBmp" Value="$dialogBmp" />
    <WixVariable Id="WixUIBannerBmp" Value="$bannerBmp" />
    <WixVariable Id="WixUILicenseRtf" Value="$licenseRtf" />

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteFolder" Name="NexMote">
        <Directory Id="INSTALLFOLDER" Name="Agent" />
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
                    WorkingDirectory="INSTALLFOLDER"
                    Icon="NexMoteIco" />
          <RemoveFolder Id="CleanNexMoteProgramsFolder" On="uninstall" />
          <RegistryValue Root="HKCU" Key="Software\NexMote\Agent" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
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
                  WorkingDirectory="INSTALLFOLDER"
                  Icon="NexMoteIco" />
        <RegistryValue Root="HKCU" Key="Software\NexMote\Agent" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
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
    <MediaTemplate EmbedCab="yes" />

    <!-- Icon & Control Panel (Add/Remove Programs) Branding -->
    <Icon Id="NexMoteTechIco" SourceFile="$iconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteTechIco" />
    <Property Id="ARPHELPLINK" Value="https://nexmote.com" />
    <Property Id="ARPURLINFOABOUT" Value="https://nexmote.com" />
    <Property Id="ARPURLUPDATEINFO" Value="https://nexmote.com/downloads" />
    <Property Id="ARPCONTACT" Value="destek@nexmote.com" />
    <Property Id="ARPCOMMENTS" Value="NexMote Uzaktan Masaüstü Yönetim ve Teknisyen Konsolu" />
    <Property Id="ARPNOREPAIR" Value="yes" />

    <!-- Modern WiX UI Wizard -->
    <ui:WixUI Id="WixUI_InstallDir" InstallDirectory="INSTALLFOLDERTECH" />
    <WixVariable Id="WixUIDialogBmp" Value="$dialogBmp" />
    <WixVariable Id="WixUIBannerBmp" Value="$bannerBmp" />
    <WixVariable Id="WixUILicenseRtf" Value="$licenseRtf" />

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
          <RegistryValue Root="HKCU" Key="Software\NexMote\Technician" Name="StartMenuShortcut" Type="integer" Value="1" KeyPath="yes" />
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
        <RegistryValue Root="HKCU" Key="Software\NexMote\Technician" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
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

Write-Host "Generating Agent WXS..."
$agentWxs = [System.IO.Path]::Combine($wixDir, "NexMote.Agent.wxs")
Generate-AgentWxs -pkgDir $agentPkgDir -outputWxs $agentWxs

Write-Host "Building NexMote-Agent-Setup.msi..."
$agentMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Agent-Setup.msi")
wix build $agentWxs -ext WixToolset.UI.wixext -ext WixToolset.Util.wixext -o $agentMsi

Write-Host "Generating Technician WXS..."
$techWxs = [System.IO.Path]::Combine($wixDir, "NexMote.Technician.wxs")
Generate-TechnicianWxs -pkgDir $techPkgDir -outputWxs $techWxs

Write-Host "Building NexMote-Technician-Setup.msi..."
$techMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Technician-Setup.msi")
wix build $techWxs -ext WixToolset.UI.wixext -o $techMsi

Write-Host "MSI Packages built successfully with Enterprise UI:"
Write-Host "  - $agentMsi"
Write-Host "  - $techMsi"

