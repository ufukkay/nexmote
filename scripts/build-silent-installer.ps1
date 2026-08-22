[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPassword,
    [string]$AdminUsername = "ITDestek",
    [string]$AdminDomain = ".",
    [string]$MsiPath,
    [string]$OutputDir
)

# ONEMLI GUVENLIK NOTU:
# Bu script, verdiginiz admin kimlik bilgisini derlenen .exe'nin icine GOMER.
# Bu repo GitHub'da PUBLIC oldugu icin:
#  - Bu script'in ASLA -AdminPassword degerini kod icine sabit (hardcoded) yazmayin,
#    her zaman parametre olarak calisma anında verin.
#  - Uretilen Credentials.Generated.cs dosyasi .gitignore'da - build sonrasi otomatik silinir,
#    yine de "git status" ile hicbir zaman staged/tracked olmadigini teyit edin.
#  - Cikti klasorundeki .exe + .msi'i SADECE hedef kisiye ozel bir kanaldan (dogrudan
#    indirme linki, sifreli mail eki) gonderin - repo'nun herkese acik downloads/
#    klasorune KESINLIKLE eklemeyin, DownloadCatalog.cs'e kaydetmeyin.
#  - Kurulum tamamlaninca (cihaz panelde cevrimici gorununce) bu hesabin sifresini
#    degistirin - .exe elden cikarsa/sizarsa gomulu sifre gecersiz olsun.

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

if (-not $MsiPath) {
    $MsiPath = Join-Path $root "downloads\NexMote-Agent-Setup.msi"
}
if (-not (Test-Path $MsiPath)) {
    throw "MSI bulunamadi: $MsiPath - once scripts\package-windows.ps1 ile normal Agent paketini olusturun."
}

if (-not $OutputDir) {
    $OutputDir = Join-Path $root "artifacts\private-installers"
}

$projectDir = Join-Path $root "src\NexMote.Agent.SilentInstaller"
$projectFile = Join-Path $projectDir "NexMote.Agent.SilentInstaller.csproj"
$credFile = Join-Path $projectDir "Credentials.Generated.cs"

$escapedDomain = $AdminDomain.Replace('\', '\\').Replace('"', '\"')
$escapedUsername = $AdminUsername.Replace('\', '\\').Replace('"', '\"')
$escapedPassword = $AdminPassword.Replace('\', '\\').Replace('"', '\"')

$credSource = @"
// BU DOSYA scripts\build-silent-installer.ps1 TARAFINDAN OTOMATIK URETILIR.
// Admin kimlik bilgisi icerir - ASLA git'e eklemeyin (.gitignore'da listelidir).
// Build tamamlandiktan hemen sonra script tarafindan silinir.
internal static class Credentials
{
    public const string Domain = "$escapedDomain";
    public const string Username = "$escapedUsername";
    public const string Password = "$escapedPassword";
}
"@

Set-Content -LiteralPath $credFile -Value $credSource -Encoding UTF8 -NoNewline

try {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

    & $dotnet publish $projectFile -c Release -r win-x64 --self-contained true `
        /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutputDir
    if ($LASTEXITCODE -ne 0) {
        throw "Build basarisiz oldu (exit code $LASTEXITCODE)."
    }

    Copy-Item -LiteralPath $MsiPath -Destination (Join-Path $OutputDir "NexMote-Agent-Setup.msi") -Force

    Write-Host ""
    Write-Host "Sessiz kurulum paketi hazir: $OutputDir"
    Write-Host "  - NexMote.Agent.SilentInstaller.exe"
    Write-Host "  - NexMote-Agent-Setup.msi (ayni klasorde, ayni zip icinde kalmali)"
    Write-Host ""
    Write-Host "ONEMLI:"
    Write-Host "  1) Bu klasoru SADECE hedef kisiye ozel/dogrudan bir kanaldan gonderin."
    Write-Host "  2) Repo'nun herkese acik downloads/ klasorune veya GitHub'a KESINLIKLE eklemeyin."
    Write-Host "  3) Cihaz panelde cevrimici gorununce '$AdminUsername' hesabinin sifresini degistirin."
}
finally {
    if (Test-Path $credFile) {
        Remove-Item -LiteralPath $credFile -Force
    }
}
