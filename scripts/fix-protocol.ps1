$binPath = "C:\Users\ufuk.kaya\Desktop\Projeler\NexMote\artifacts\package\technician\NexMote.TechnicianApp.exe"
$cmd = "`"$binPath`" `"%1`""

Write-Host "Updating HKCU nexmote protocol handler..."
New-Item -Path "HKCU:\Software\Classes\nexmote\shell\open\command" -Force | Out-Null
$key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Software\Classes\nexmote\shell\open\command", $true)
if ($key) {
    $key.SetValue("", $cmd)
    $key.Close()
}

Write-Host "Updating HKLM nexmote protocol handler..."
try {
    $keyL = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey("SOFTWARE\Classes\nexmote\shell\open\command", $true)
    if ($keyL) {
        $keyL.SetValue("", $cmd)
        $keyL.Close()
    }
} catch {
    Write-Host "HKLM permission skipped."
}

Write-Host "Protocol handler registered cleanly to: $cmd"
