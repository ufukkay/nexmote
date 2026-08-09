@echo off
:: NexMote Agent Kurulum Yardımcısı
cd /d "%~dp0"
echo NexMote Agent Kuruluyor...
powershell -ExecutionPolicy Bypass -File "%~dp0install-agent.ps1"
if %errorlevel% neq 0 (
    echo.
    echo Kurulum yonetici haklari gerektirebilir. Lutfen 'Yonetici olarak calistir' secenegi ile tekrar deneyin.
    pause
) else (
    echo.
    echo NexMote Agent basariyla kuruldu. Tray uygulamasi baslatiliyor...
    schtasks /run /tn "NexMote Agent Tray" >nul 2>&1
    if exist "%ProgramFiles%\NexMote\Agent\NexMote.Agent.Tray.exe" (
        start "" "%ProgramFiles%\NexMote\Agent\NexMote.Agent.Tray.exe"
    )
    timeout /t 3
)
