[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:ProgramData "NexMote\Logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ("technician-install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $logPath -Append | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

try {
$installDir = Join-Path $env:LOCALAPPDATA "NexMote\Technician"
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$desktopDir = [Environment]::GetFolderPath("Desktop")

Write-Step "Kurulum klasörü hazırlanıyor: $installDir"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

Write-Step "Dosyalar kopyalanıyor."
Get-ChildItem -LiteralPath $sourceDir -Force |
    Where-Object { $_.Name -notin @("install-technician.ps1", "uninstall-technician.ps1", "README.txt") } |
    Copy-Item -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir "NexMote.TechnicianApp.exe"
if (-not (Test-Path $exePath)) {
    throw "NexMote.TechnicianApp.exe bulunamadi."
}

Write-Step "nexmote:// protokol kaydı yazılıyor."
$protocolRoot = "HKCU:\Software\Classes\nexmote"
New-Item -Path $protocolRoot -Force | Out-Null
Set-Item -Path $protocolRoot -Value "URL:NexMote Protocol"
New-ItemProperty -Path $protocolRoot -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null

New-Item -Path "$protocolRoot\DefaultIcon" -Force | Out-Null
Set-Item -Path "$protocolRoot\DefaultIcon" -Value "$exePath,1"

New-Item -Path "$protocolRoot\shell\open\command" -Force | Out-Null
Set-Item -Path "$protocolRoot\shell\open\command" -Value "`"$exePath`" `"%1`""

if ($desktopDir) {
    Write-Step "Masaüstü kısayolu oluşturuluyor."
    $shortcutPath = Join-Path $desktopDir "NexMote Technician.lnk"
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $exePath
    $shortcut.WorkingDirectory = $installDir
    $shortcut.Description = "NexMote Technician App"
    $shortcut.IconLocation = "$exePath,0"
    $shortcut.Save()
}

Write-Step "NexMote Technician App kuruldu."
Write-Step "Log dosyası: $logPath"
}
catch {
    Write-Step "HATA: $($_.Exception.Message)"
    Write-Step "Stack: $($_.ScriptStackTrace)"
    throw
}
finally {
    Stop-Transcript | Out-Null
}
