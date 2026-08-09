param(
    [string]$ServerUrl = "http://127.0.0.1:5080",
    [string]$EnrollmentKey = "dev-enrollment-key"
)

$ErrorActionPreference = "Stop"
$rootDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, ".."))
$agentPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "agent")
$techPkgDir = [System.IO.Path]::Combine($rootDir, "artifacts", "package", "technician")
$downloadsDir = [System.IO.Path]::Combine($rootDir, "downloads")

New-Item -ItemType Directory -Path $downloadsDir -Force | Out-Null

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
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="NexMote Agent"
           Manufacturer="NexMote Inc."
           Version="0.1.0"
           UpgradeCode="A76F12C0-94A1-420E-B6D7-90E0F3628101"
           Scope="perMachine">

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="NexMote Agent uygulamasının daha yeni bir sürümü zaten kurulu." />
    <MediaTemplate EmbedCab="yes" />

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteFolder" Name="NexMote">
        <Directory Id="INSTALLFOLDER" Name="Agent" />
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="DesktopFolder">
      <Component Id="AgentDesktopShortcutComp" Guid="A9812A4F-821B-4190-84E1-912A09B2E801">
        <Shortcut Id="AgentDesktopShortcut"
                  Name="NexMote Agent"
                  Description="NexMote Ajanı Masaüstü İstemcisi"
                  Target="[INSTALLFOLDER]NexMote.Agent.Tray.exe"
                  WorkingDirectory="INSTALLFOLDER" />
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
                        Description="NexMote Kurumsal Uzaktan Yönetim Ajanı Servisi"
                        Start="auto"
                        Account="LocalSystem"
                        ErrorControl="normal" />
        <ServiceControl Id="ServiceControler"
                        Name="NexMote Agent"
                        Start="install"
                        Stop="both"
                        Remove="uninstall"
                        Wait="yes" />
      </Component>

      <Component Id="AgentTrayExeComponent">
        <File Id="AgentTrayExe" Source="$pkgDir\NexMote.Agent.Tray.exe" KeyPath="yes" />
        <RegistryValue Root="HKLM"
                       Key="SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
                       Name="NexMote Agent Tray"
                       Value="&quot;[INSTALLFOLDER]NexMote.Agent.Tray.exe&quot;"
                       Type="string" />
      </Component>

$fileElements
    </ComponentGroup>

    <CustomAction Id="LaunchTrayApp"
                  FileRef="AgentTrayExe"
                  ExeCommand=""
                  Execute="immediate"
                  Return="asyncNoWait" />

    <InstallExecuteSequence>
      <Custom Action="LaunchTrayApp" After="InstallFinalize" Condition="NOT Installed OR REINSTALL" />
    </InstallExecuteSequence>

    <Feature Id="MainFeature" Title="NexMote Agent" Level="1">
      <ComponentGroupRef Id="AgentServiceComponents" />
      <ComponentRef Id="AgentDesktopShortcutComp" />
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
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package Name="NexMote Technician Console"
           Manufacturer="NexMote Inc."
           Version="0.1.0"
           UpgradeCode="B87F12C0-94A1-420E-B6D7-90E0F3628102"
           Scope="perMachine">

    <MajorUpgrade AllowSameVersionUpgrades="yes" DowngradeErrorMessage="NexMote Technician Console uygulamasının daha yeni bir sürümü zaten kurulu." />
    <MediaTemplate EmbedCab="yes" />

    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteFolderTech" Name="NexMote">
        <Directory Id="INSTALLFOLDERTECH" Name="Technician" />
      </Directory>
    </StandardDirectory>

    <StandardDirectory Id="DesktopFolder">
      <Component Id="DesktopShortcutComp" Guid="C9812A4F-821B-4190-84E1-912A09B2E810">
        <Shortcut Id="DesktopShortcut"
                  Name="NexMote Technician Console"
                  Description="NexMote Uzaktan Destek İstemcisi"
                  Target="[INSTALLFOLDERTECH]NexMote.TechnicianApp.exe"
                  WorkingDirectory="INSTALLFOLDERTECH" />
        <RegistryValue Root="HKCU" Key="Software\NexMote\Technician" Name="DesktopShortcut" Type="integer" Value="1" KeyPath="yes" />
      </Component>
    </StandardDirectory>

    <ComponentGroup Id="TechComponents" Directory="INSTALLFOLDERTECH">
      <Component Id="TechMainExeComponent">
        <File Id="TechMainExe" Source="$pkgDir\NexMote.TechnicianApp.exe" KeyPath="yes" />
        
        <!-- Protocol Handler Registration -->
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
    </Feature>
  </Package>
</Wix>
"@

    Set-Content -Path $outputWxs -Value $wxsContent -Encoding UTF8
}

Write-Host "Generating Agent WXS..."
$agentWxs = [System.IO.Path]::Combine($rootDir, "installer", "agent", "NexMote.Agent.wxs")
Generate-AgentWxs -pkgDir $agentPkgDir -outputWxs $agentWxs

Write-Host "Building NexMote-Agent-Setup.msi..."
$agentMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Agent-Setup.msi")
wix build $agentWxs -o $agentMsi

Write-Host "Generating Technician WXS..."
$techWxs = [System.IO.Path]::Combine($rootDir, "installer", "technician", "NexMote.Technician.wxs")
Generate-TechnicianWxs -pkgDir $techPkgDir -outputWxs $techWxs

Write-Host "Building NexMote-Technician-Setup.msi..."
$techMsi = [System.IO.Path]::Combine($downloadsDir, "NexMote-Technician-Setup.msi")
wix build $techWxs -o $techMsi

Write-Host "MSI Packages built successfully:"
Write-Host "  - $agentMsi"
Write-Host "  - $techMsi"
