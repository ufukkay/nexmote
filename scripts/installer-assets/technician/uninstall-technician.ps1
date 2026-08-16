[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:ProgramData "NexMote\Logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ("technician-uninstall-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $logPath -Append | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

try {
$installDir = Join-Path $env:LOCALAPPDATA "NexMote\Technician"
$protocolRoot = "HKCU:\Software\Classes\nexmote"
$desktopDir = [Environment]::GetFolderPath("Desktop")

if (Test-Path $protocolRoot) {
    Write-Step "nexmote:// protokol kaydı siliniyor."
    Remove-Item -LiteralPath $protocolRoot -Recurse -Force
}

if (Test-Path $installDir) {
    Write-Step "Kurulum klasörü siliniyor: $installDir"
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

if ($desktopDir) {
    Write-Step "Masaüstü kısayolu siliniyor."
    Remove-Item -LiteralPath (Join-Path $desktopDir "NexMote Technician.lnk") -Force -ErrorAction SilentlyContinue
}

Write-Step "NexMote Technician App kaldırıldı."
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
