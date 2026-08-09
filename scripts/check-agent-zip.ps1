$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead("downloads\nexmote-agent-win-x64.zip")
try {
    $zip.Entries |
        Where-Object { $_.FullName -like "*Tray*" -or $_.FullName -like "*Agent.Windows.exe" } |
        Select-Object FullName, Length |
        Format-Table -AutoSize
}
finally {
    $zip.Dispose()
}
