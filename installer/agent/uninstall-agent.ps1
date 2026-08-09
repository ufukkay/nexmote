[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:ProgramData "NexMote\Logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ("agent-uninstall-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $logPath -Append | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

try {
$serviceName = "NexMote Agent"
$installDir = Join-Path $env:ProgramFiles "NexMote\Agent"
$runKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$runName = "NexMote Agent Tray"
$desktopDir = [Environment]::GetFolderPath("CommonDesktopDirectory")

Write-Step "Tray süreci kapatılıyor."
Get-Process -Name "NexMote.Agent.Tray" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

if (Test-Path $runKey) {
    Write-Step "Tray otomatik başlangıç kaydı siliniyor."
    Remove-ItemProperty -Path $runKey -Name $runName -Force -ErrorAction SilentlyContinue
}

if ($desktopDir) {
    Write-Step "Masaüstü kısayolu siliniyor."
    Remove-Item -LiteralPath (Join-Path $desktopDir "NexMote Agent.lnk") -Force -ErrorAction SilentlyContinue
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Servis durduruluyor ve siliniyor."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

if (Test-Path $installDir) {
    Write-Step "Kurulum klasörü siliniyor: $installDir"
    Remove-Item -LiteralPath $installDir -Recurse -Force
}

Write-Step "NexMote Agent kaldırıldı."
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
