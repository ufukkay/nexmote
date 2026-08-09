[CmdletBinding()]
param(
    [string]$ServerUrl
)

$ErrorActionPreference = "Stop"

$logDir = Join-Path $env:ProgramData "NexMote\Logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logPath = Join-Path $logDir ("agent-install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
Start-Transcript -Path $logPath -Append | Out-Null

function Write-Step {
    param([string]$Message)
    Write-Host ("[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $Message)
}

try {
$serviceName = "NexMote Agent"
$installDir = Join-Path $env:ProgramFiles "NexMote\Agent"
$sourceDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$runKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
$runName = "NexMote Agent Tray"
$desktopDir = [Environment]::GetFolderPath("CommonDesktopDirectory")

if ($ServerUrl) {
    Write-Step "ServerUrl override uygulanıyor: $ServerUrl"
    $configPath = Join-Path $sourceDir "appsettings.json"
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    $config.Agent.ServerUrl = $ServerUrl
    $config | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $configPath -Encoding UTF8
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Step "Mevcut servis durduruluyor ve siliniyor."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    & sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

Write-Step "Kurulum klasörü hazırlanıyor: $installDir"
New-Item -ItemType Directory -Force -Path $installDir | Out-Null

Write-Step "Dosyalar kopyalanıyor."
Get-ChildItem -LiteralPath $sourceDir -Force |
    Where-Object { $_.Name -notin @("install-agent.ps1", "uninstall-agent.ps1", "README.txt") } |
    Copy-Item -Destination $installDir -Recurse -Force

$exePath = Join-Path $installDir "NexMote.Agent.Windows.exe"
if (-not (Test-Path $exePath)) {
    throw "NexMote.Agent.Windows.exe bulunamadi."
}

Write-Step "Windows servisi oluşturuluyor."
& sc.exe create $serviceName binPath= "`"$exePath`"" start= auto DisplayName= "NexMote Agent" | Out-Null
& sc.exe description $serviceName "NexMote endpoint agent service." | Out-Null
Write-Step "Windows servisi başlatılıyor."
Start-Service -Name $serviceName

$trayPath = Join-Path $installDir "NexMote.Agent.Tray.exe"
if (Test-Path $trayPath) {
    Write-Step "Tray otomatik başlangıç kaydı yazılıyor."
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name $runName -Value "`"$trayPath`"" -PropertyType String -Force | Out-Null

    if ($desktopDir) {
        Write-Step "Masaüstü kısayolu oluşturuluyor."
        $shortcutPath = Join-Path $desktopDir "NexMote Agent.lnk"
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $trayPath
        $shortcut.WorkingDirectory = $installDir
        $shortcut.Description = "NexMote Agent"
        $shortcut.IconLocation = "$trayPath,0"
        $shortcut.Save()
    }

    Write-Step "Tray uygulaması başlatılıyor."
    Start-Process -FilePath $trayPath -ErrorAction SilentlyContinue
}

Write-Step "NexMote Agent kuruldu ve başlatıldı."
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
