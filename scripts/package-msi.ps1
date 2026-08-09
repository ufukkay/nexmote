[CmdletBinding()]
param(
    [string]$ServerUrl = "http://127.0.0.1:5080",
    [string]$EnrollmentKey = "dev-enrollment-key",
    [string]$SigningCertificate = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$rootFullPath = [System.IO.Path]::GetFullPath($root)
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
$wix = Join-Path $root ".tools-wix4\wix.exe"
if (-not (Test-Path $wix)) {
    $wix = Join-Path $root ".tools\wix.exe"
}
$downloads = Join-Path $root "downloads"
$artifacts = Join-Path $root "artifacts\msi"
$agentPublish = Join-Path $artifacts "publish\agent"
$technicianPublish = Join-Path $artifacts "publish\technician"
$wixWork = Join-Path $artifacts "wix"
$agentProject = Join-Path $root "agent-windows\src\NexMote.Agent.Windows\NexMote.Agent.Windows.csproj"
$trayProject = Join-Path $root "agent-windows\src\NexMote.Agent.Tray\NexMote.Agent.Tray.csproj"
$technicianProject = Join-Path $root "technician-app\src\NexMote.TechnicianApp\NexMote.TechnicianApp.csproj"
$iconPath = Join-Path $root "assets\nexmote.ico"

function Assert-UnderRoot {
    param([string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootFullPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside NexMote root: $fullPath"
    }
}

function Escape-Xml {
    param([string]$Value)
    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-ComponentId {
    param([string]$Prefix, [string]$Name)

    $safe = [Regex]::Replace($Name, "[^A-Za-z0-9_]", "_")
    if ($safe.Length -gt 46) {
        $safe = $safe.Substring(0, 46)
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Name)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $sha = $sha256.ComputeHash($bytes)
    }
    finally {
        $sha256.Dispose()
    }
    $hash = (($sha | ForEach-Object { $_.ToString("x2") }) -join "").Substring(0, 10).ToUpperInvariant()
    return "$Prefix`_$safe`_$hash"
}

function Publish-App {
    param(
        [string]$Project,
        [string]$Output
    )

    & $dotnet publish $Project -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o $Output
    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed: $Project"
    }
}

function Sign-Files {
    param([string[]]$Paths)

    if ([string]::IsNullOrWhiteSpace($SigningCertificate)) {
        Write-Warning "No code-signing certificate was supplied. Smart App Control may block these development packages."
        return
    }

    if (-not (Test-Path $SigningCertificate)) {
        throw "Signing certificate not found: $SigningCertificate"
    }

    $signToolPath = $null
    $signToolCommand = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($signToolCommand) {
        $signToolPath = $signToolCommand.Source
    }
    if (-not $signToolPath) {
        $sdkTool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
            Sort-Object FullName |
            Select-Object -Last 1
        if ($sdkTool) {
            $signToolPath = $sdkTool.FullName
        }
    }

    if (-not $signToolPath) {
        throw "signtool.exe not found. Install the Windows SDK or add signtool.exe to PATH."
    }

    $signArguments = @(
        "sign",
        "/fd", "SHA256",
        "/td", "SHA256",
        "/tr", "http://timestamp.digicert.com",
        "/f", $SigningCertificate
    )
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArguments += @("/p", $CertificatePassword)
    }

    foreach ($path in $Paths) {
        if (-not (Test-Path $path)) {
            throw "Signing target not found: $path"
        }

        & $signToolPath @signArguments $path
        if ($LASTEXITCODE -ne 0) {
            throw "Code signing failed: $path"
        }
    }
}


function Write-UninstallerFiles {
    param(
        [string]$OutputDir,
        [string]$DisplayName
    )

    $cmd = @"
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1"
exit /b %ERRORLEVEL%
"@

    $ps1 = @"
`$ErrorActionPreference = "Stop"

`$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
`$principal = [Security.Principal.WindowsPrincipal]::new(`$currentIdentity)
`$isAdmin = `$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not `$isAdmin) {
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", `$PSCommandPath)
    exit
}

`$logPath = Join-Path `$PSScriptRoot "uninstall-log.txt"
Start-Transcript -Path `$logPath -Append | Out-Null

try {
    Write-Host "NexMote uninstall started: `$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host "Package: $DisplayName"

    `$keys = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )

    `$app = Get-ItemProperty -Path `$keys -ErrorAction SilentlyContinue |
        Where-Object { `$_.DisplayName -eq "$DisplayName" } |
        Select-Object -First 1

    if (-not `$app) {
        throw "Installed package not found: $DisplayName"
    }

    Write-Host "Product code: `$(`$app.PSChildName)"
    `$process = Start-Process -FilePath "msiexec.exe" -ArgumentList "/x `$(`$app.PSChildName)" -Wait -PassThru
    Write-Host "msiexec exit code: `$(`$process.ExitCode)"
    exit `$process.ExitCode
}
catch {
    Write-Host "ERROR: `$(`$_.Exception.Message)"
    throw
}
finally {
    Stop-Transcript | Out-Null
}
"@

    Set-Content -LiteralPath (Join-Path $OutputDir "uninstall.cmd") -Value $cmd -Encoding ASCII
    Set-Content -LiteralPath (Join-Path $OutputDir "uninstall.ps1") -Value $ps1 -Encoding UTF8
}

function Write-AgentWxs {
    param([string]$PublishDir, [string]$OutputPath, [string]$LicensePath)

    $files = Get-ChildItem -LiteralPath $PublishDir -File | Sort-Object Name
    $mainExe = $files | Where-Object { $_.Name -eq "NexMote.Agent.Windows.exe" } | Select-Object -First 1
    if (-not $mainExe) {
        throw "NexMote.Agent.Windows.exe not found in $PublishDir"
    }

    $components = New-Object System.Text.StringBuilder
    foreach ($file in $files) {
        $componentId = Get-ComponentId "AgentCmp" $file.Name
        $fileId = Get-ComponentId "AgentFile" $file.Name
        $source = Escape-Xml $file.FullName

        [void]$components.AppendLine("      <Component Id=`"$componentId`" Guid=`"*`">")
        [void]$components.AppendLine("        <File Id=`"$fileId`" Source=`"$source`" KeyPath=`"yes`" />")
        if ($file.Name -eq "NexMote.Agent.Windows.exe") {
            [void]$components.AppendLine("        <ServiceInstall Id=`"NexMoteAgentServiceInstall`" Type=`"ownProcess`" Name=`"NexMote Agent`" DisplayName=`"!(loc.AgentServiceName)`" Description=`"!(loc.AgentServiceDescription)`" Start=`"auto`" Account=`"LocalSystem`" ErrorControl=`"normal`" />")
            [void]$components.AppendLine("        <ServiceControl Id=`"NexMoteAgentServiceControl`" Name=`"NexMote Agent`" Start=`"install`" Stop=`"both`" Remove=`"uninstall`" Wait=`"no`" />")
        }
        if ($file.Name -eq "NexMote.Agent.Tray.exe") {
            [void]$components.AppendLine("        <RegistryValue Root=`"HKLM`" Key=`"SOFTWARE\Microsoft\Windows\CurrentVersion\Run`" Name=`"NexMote Agent Tray`" Value=`"&quot;[INSTALLFOLDER]NexMote.Agent.Tray.exe&quot;`" Type=`"string`" />")
            [void]$components.AppendLine("        <Shortcut Id=`"NexMoteAgentDesktopShortcut`" Directory=`"DesktopFolder`" Name=`"!(loc.AgentShortcutName)`" Description=`"!(loc.AgentShortcutDescription)`" Target=`"[INSTALLFOLDER]NexMote.Agent.Tray.exe`" WorkingDirectory=`"INSTALLFOLDER`" Icon=`"NexMoteIcon.ico`" />")
        }
        [void]$components.AppendLine("      </Component>")
    }

    $escapedIconPath = Escape-Xml $iconPath
    $escapedLicensePath = Escape-Xml $LicensePath
    $escapedServerUrl = Escape-Xml $ServerUrl
    $content = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="!(loc.AgentPackageName)" Manufacturer="!(loc.ManufacturerName)" Version="0.1.1.0" UpgradeCode="9E9842F2-B1D7-4AA4-8D80-3219B9F75A61" Scope="perMachine">
    <MajorUpgrade DowngradeErrorMessage="A newer version of NexMote Agent is already installed." />
    <MediaTemplate EmbedCab="yes" />
    <Property Id="MsiLogging" Value="voicewarmupx" />
    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLFOLDER" />
    <Icon Id="NexMoteIcon.ico" SourceFile="$escapedIconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteIcon.ico" />
    <WixVariable Id="WixUILicenseRtf" Value="$escapedLicensePath" />
    <ui:WixUI Id="WixUI_InstallDir" />
    <CustomAction Id="WriteAgentInstallLog" Directory="INSTALLFOLDER" ExeCommand="cmd.exe /c echo NexMote Agent install completed %DATE% %TIME% &gt; &quot;[INSTALLFOLDER]install-log.txt&quot; &amp; echo InstallFolder=[INSTALLFOLDER] &gt;&gt; &quot;[INSTALLFOLDER]install-log.txt&quot; &amp; echo ServerUrl=$escapedServerUrl &gt;&gt; &quot;[INSTALLFOLDER]install-log.txt&quot; &amp; echo ServiceLog=C:ProgramDataNexMoteLogsagent-service.log &gt;&gt; &quot;[INSTALLFOLDER]install-log.txt&quot;" Execute="immediate" Return="ignore" />
    <CustomAction Id="LaunchNexMoteAgentTray" Directory="INSTALLFOLDER" ExeCommand="cmd.exe /c start &quot;&quot; &quot;[INSTALLFOLDER]NexMote.Agent.Tray.exe&quot;" Execute="immediate" Return="asyncNoWait" />
    <InstallExecuteSequence>
      <Custom Action="WriteAgentInstallLog" After="InstallFinalize" Condition="NOT Installed" />
      <Custom Action="LaunchNexMoteAgentTray" After="WriteAgentInstallLog" Condition="NOT Installed" />
    </InstallExecuteSequence>
    <Feature Id="MainFeature" Title="!(loc.AgentFeatureTitle)" Level="1">
      <ComponentGroupRef Id="AgentComponents" />
    </Feature>
  </Package>

  <Fragment>
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteRootDir" Name="NexMote">
        <Directory Id="INSTALLFOLDER" Name="Agent" />
      </Directory>
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder" />
  </Fragment>

  <Fragment>
    <ComponentGroup Id="AgentComponents" Directory="INSTALLFOLDER">
$components
    </ComponentGroup>
  </Fragment>
</Wix>
"@

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
}

function Write-TechnicianWxs {
    param([string]$PublishDir, [string]$OutputPath, [string]$LicensePath)

    $files = Get-ChildItem -LiteralPath $PublishDir -File | Sort-Object Name
    $mainExe = $files | Where-Object { $_.Name -eq "NexMote.TechnicianApp.exe" } | Select-Object -First 1
    if (-not $mainExe) {
        throw "NexMote.TechnicianApp.exe not found in $PublishDir"
    }

    $components = New-Object System.Text.StringBuilder
    foreach ($file in $files) {
        $componentId = Get-ComponentId "TechnicianCmp" $file.Name
        $fileId = Get-ComponentId "TechnicianFile" $file.Name
        $source = Escape-Xml $file.FullName

        [void]$components.AppendLine("      <Component Id=`"$componentId`" Guid=`"*`">")
        [void]$components.AppendLine("        <File Id=`"$fileId`" Source=`"$source`" KeyPath=`"yes`" />")
        if ($file.Name -eq "NexMote.TechnicianApp.exe") {
            [void]$components.AppendLine("        <RegistryValue Root=`"HKLM`" Key=`"Software\Classes\nexmote`" Value=`"URL:NexMote Protocol`" Type=`"string`" />")
            [void]$components.AppendLine("        <RegistryValue Root=`"HKLM`" Key=`"Software\Classes\nexmote`" Name=`"URL Protocol`" Value=`"`" Type=`"string`" />")
            [void]$components.AppendLine("        <RegistryValue Root=`"HKLM`" Key=`"Software\Classes\nexmote\DefaultIcon`" Value=`"&quot;[INSTALLFOLDER]NexMote.TechnicianApp.exe&quot;,1`" Type=`"string`" />")
            [void]$components.AppendLine("        <RegistryValue Root=`"HKLM`" Key=`"Software\Classes\nexmote\shell\open\command`" Value=`"&quot;[INSTALLFOLDER]NexMote.TechnicianApp.exe&quot; &quot;%1&quot;`" Type=`"string`" />")
            [void]$components.AppendLine("        <Shortcut Id=`"NexMoteTechnicianDesktopShortcut`" Directory=`"DesktopFolder`" Name=`"!(loc.TechnicianShortcutName)`" Description=`"!(loc.TechnicianShortcutDescription)`" Target=`"[INSTALLFOLDER]NexMote.TechnicianApp.exe`" WorkingDirectory=`"INSTALLFOLDER`" Icon=`"NexMoteIcon.ico`" />")
        }
        [void]$components.AppendLine("      </Component>")
    }

    $escapedIconPath = Escape-Xml $iconPath
    $escapedLicensePath = Escape-Xml $LicensePath
    $content = @"
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
  <Package Name="!(loc.TechnicianPackageName)" Manufacturer="!(loc.ManufacturerName)" Version="0.1.1.0" UpgradeCode="2F11FA6B-0679-4101-B013-2D669A40C0AB" Scope="perMachine">
    <MajorUpgrade DowngradeErrorMessage="A newer version of NexMote Technician App is already installed." />
    <MediaTemplate EmbedCab="yes" />
    <Property Id="MsiLogging" Value="voicewarmupx" />
    <Property Id="WIXUI_INSTALLDIR" Value="INSTALLFOLDER" />
    <Icon Id="NexMoteIcon.ico" SourceFile="$escapedIconPath" />
    <Property Id="ARPPRODUCTICON" Value="NexMoteIcon.ico" />
    <WixVariable Id="WixUILicenseRtf" Value="$escapedLicensePath" />
    <ui:WixUI Id="WixUI_InstallDir" />
    <CustomAction Id="WriteTechnicianInstallLog" Directory="INSTALLFOLDER" ExeCommand="cmd.exe /c echo NexMote Technician install completed %DATE% %TIME% &gt; &quot;[INSTALLFOLDER]install-log.txt&quot; &amp; echo InstallFolder=[INSTALLFOLDER] &gt;&gt; &quot;[INSTALLFOLDER]install-log.txt&quot;" Execute="immediate" Return="ignore" />
    <CustomAction Id="LaunchNexMoteTechnician" Directory="INSTALLFOLDER" ExeCommand="cmd.exe /c start &quot;&quot; &quot;[INSTALLFOLDER]NexMote.TechnicianApp.exe&quot;" Execute="immediate" Return="asyncNoWait" />
    <InstallExecuteSequence>
      <Custom Action="WriteTechnicianInstallLog" After="InstallFinalize" Condition="NOT Installed" />
      <Custom Action="LaunchNexMoteTechnician" After="WriteTechnicianInstallLog" Condition="NOT Installed" />
    </InstallExecuteSequence>
    <Feature Id="MainFeature" Title="!(loc.TechnicianFeatureTitle)" Level="1">
      <ComponentGroupRef Id="TechnicianComponents" />
    </Feature>
  </Package>

  <Fragment>
    <StandardDirectory Id="ProgramFiles64Folder">
      <Directory Id="NexMoteRootDir" Name="NexMote">
        <Directory Id="INSTALLFOLDER" Name="Technician" />
      </Directory>
    </StandardDirectory>
    <StandardDirectory Id="DesktopFolder" />
  </Fragment>

  <Fragment>
    <ComponentGroup Id="TechnicianComponents" Directory="INSTALLFOLDER">
$components
    </ComponentGroup>
  </Fragment>
</Wix>
"@

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding UTF8
}

Assert-UnderRoot $downloads
Assert-UnderRoot $artifacts

if (-not (Test-Path $dotnet)) {
    throw "Local .NET SDK not found: $dotnet"
}

if (-not (Test-Path $wix)) {
    throw "WiX tool not found. Run: .\.dotnet\dotnet.exe tool install --tool-path .\.tools wix"
}

if (-not (Test-Path $iconPath)) {
    throw "NexMote icon not found: $iconPath"
}

if (Test-Path $artifacts) {
    Remove-Item -LiteralPath $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $downloads, $agentPublish, $technicianPublish, $wixWork | Out-Null

Publish-App -Project $agentProject -Output $agentPublish
Publish-App -Project $trayProject -Output $agentPublish
Publish-App -Project $technicianProject -Output $technicianPublish

Write-UninstallerFiles -OutputDir $agentPublish -DisplayName "NexMote Agent"
Write-UninstallerFiles -OutputDir $technicianPublish -DisplayName "NexMote Technician App"

$agentConfig = [ordered]@{
    Agent = [ordered]@{
        ServerUrl = $ServerUrl
        EnrollmentKey = $EnrollmentKey
        AgentVersion = "0.1.1"
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

$licenseRtf = Join-Path $wixWork "License.rtf"
$turkishLocalization = Join-Path $root "installer\msi\localization\NexMote.tr-TR.wxl"
$englishLocalization = Join-Path $root "installer\msi\localization\NexMote.en-US.wxl"

if (-not (Test-Path $turkishLocalization) -or -not (Test-Path $englishLocalization)) {
    throw "Installer localization files are missing."
}

Set-Content -LiteralPath $licenseRtf -Encoding ASCII -Value "{\rtf1\ansi NexMote internal deployment package.\par This installer is for authorized NexMote environments only.\par}"

$agentExe = Join-Path $agentPublish "NexMote.Agent.Windows.exe"
$trayExe = Join-Path $agentPublish "NexMote.Agent.Tray.exe"
$technicianExe = Join-Path $technicianPublish "NexMote.TechnicianApp.exe"
Sign-Files -Paths @($agentExe, $trayExe, $technicianExe)

$variants = @(
    [pscustomobject]@{
        Culture = "tr-TR"
        Localization = $turkishLocalization
        AgentMsi = (Join-Path $downloads "nexmote-agent-win-x64.msi")
        TechnicianMsi = (Join-Path $downloads "nexmote-technician-win-x64.msi")
    },
    [pscustomobject]@{
        Culture = "en-US"
        Localization = $englishLocalization
        AgentMsi = (Join-Path $downloads "nexmote-agent-win-x64-en.msi")
        TechnicianMsi = (Join-Path $downloads "nexmote-technician-win-x64-en.msi")
    }
)

foreach ($variant in $variants) {
    $agentWxs = Join-Path $wixWork "NexMote.Agent.$($variant.Culture).wxs"
    $technicianWxs = Join-Path $wixWork "NexMote.Technician.$($variant.Culture).wxs"

    Write-AgentWxs -PublishDir $agentPublish -OutputPath $agentWxs -LicensePath $licenseRtf
    Write-TechnicianWxs -PublishDir $technicianPublish -OutputPath $technicianWxs -LicensePath $licenseRtf
    Remove-Item -LiteralPath $variant.AgentMsi, $variant.TechnicianMsi -Force -ErrorAction SilentlyContinue

    & $wix build $agentWxs -arch x64 -ext WixToolset.UI.wixext -culture $variant.Culture -loc $variant.Localization -o $variant.AgentMsi
    if ($LASTEXITCODE -ne 0) {
        throw "Agent MSI build failed for $($variant.Culture)."
    }

    & $wix build $technicianWxs -arch x64 -ext WixToolset.UI.wixext -culture $variant.Culture -loc $variant.Localization -o $variant.TechnicianMsi
    if ($LASTEXITCODE -ne 0) {
        throw "Technician MSI build failed for $($variant.Culture)."
    }

    Sign-Files -Paths @($variant.AgentMsi, $variant.TechnicianMsi)
    Write-Host "Created $($variant.AgentMsi)"
    Write-Host "Created $($variant.TechnicianMsi)"
}

Write-Host "Agent ServerUrl: $ServerUrl"
