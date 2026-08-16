Add-Type -AssemblyName System.Drawing

$rootDir = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($PSScriptRoot, ".."))
$installerAssetsDir = [System.IO.Path]::Combine($rootDir, "assets", "installer")
New-Item -ItemType Directory -Path $installerAssetsDir -Force | Out-Null

$icoPath = [System.IO.Path]::Combine($rootDir, "assets", "nexmote.ico")
$icon = if (Test-Path $icoPath) { New-Object System.Drawing.Icon($icoPath, 128, 128) } else { $null }

# 1. Dialog.bmp (493 x 312)
$dialogBmp = New-Object System.Drawing.Bitmap(493, 312, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g1 = [System.Drawing.Graphics]::FromImage($dialogBmp)
$g1.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g1.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

# Draw deep blue/slate gradient
$rect1 = New-Object System.Drawing.Rectangle(0, 0, 493, 312)
$brush1 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect1,
    [System.Drawing.Color]::FromArgb(15, 23, 42), # #0F172A (Deep Slate)
    [System.Drawing.Color]::FromArgb(30, 58, 138), # #1E3A8A (Royal Blue)
    [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
)
$g1.FillRectangle($brush1, $rect1)

# Draw decorative soft circles / glow
$glowBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(25, 37, 99, 235))
$g1.FillEllipse($glowBrush, 80, 40, 320, 320)

# Draw Icon if available
if ($icon -ne $null) {
    $iconBmp = $icon.ToBitmap()
    $g1.DrawImage($iconBmp, 32, 45, 72, 72)
}

# Draw Typography
$fontTitle = New-Object System.Drawing.Font("Segoe UI", 20, [System.Drawing.FontStyle]::Bold)
$fontSub = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Regular)
$fontDesc = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Regular)

$whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
$cyanBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(96, 165, 250)) # #60A5FA
$grayBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(203, 213, 225)) # #CBD5E1

$g1.DrawString("NexMote", $fontTitle, $whiteBrush, 115, 45)
$g1.DrawString("Kurumsal Uzaktan Yönetim & Destek", $fontSub, $cyanBrush, 117, 85)

$descText = "Hızlı, güvenli ve yüksek performanslı uzaktan masaüstü`nkontrolü, donanım telemetrisi ve komut konsolu."
$g1.DrawString($descText, $fontDesc, $grayBrush, 32, 140)

$g1.Dispose()
$dialogPath = [System.IO.Path]::Combine($installerAssetsDir, "dialog.bmp")
$dialogBmp.Save($dialogPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$dialogBmp.Dispose()
Write-Host "Created: $dialogPath"

# 2. Banner.bmp (493 x 58)
$bannerBmp = New-Object System.Drawing.Bitmap(493, 58, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$g2 = [System.Drawing.Graphics]::FromImage($bannerBmp)
$g2.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g2.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$rect2 = New-Object System.Drawing.Rectangle(0, 0, 493, 58)
$brush2 = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
    $rect2,
    [System.Drawing.Color]::FromArgb(15, 23, 42),
    [System.Drawing.Color]::FromArgb(37, 99, 235),
    [System.Drawing.Drawing2D.LinearGradientMode]::Horizontal
)
$g2.FillRectangle($brush2, $rect2)

if ($icon -ne $null) {
    $iconBmpSmall = $icon.ToBitmap()
    $g2.DrawImage($iconBmpSmall, 445, 10, 36, 36)
}

$fontBannerTitle = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$fontBannerSub = New-Object System.Drawing.Font("Segoe UI", 8.5, [System.Drawing.FontStyle]::Regular)

$g2.DrawString("NexMote Kurulum Sihirbazı", $fontBannerTitle, $whiteBrush, 15, 10)
$g2.DrawString("Lütfen kurulum adımlarını takip edin.", $fontBannerSub, $grayBrush, 15, 32)

$g2.Dispose()
$bannerPath = [System.IO.Path]::Combine($installerAssetsDir, "banner.bmp")
$bannerBmp.Save($bannerPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
$bannerBmp.Dispose()
Write-Host "Created: $bannerPath"

# 3. License.rtf
$licenseRtf = @"
{\rtf1\ansi\ansicpg1254\deff0\nouicompat\deflang1055{\fonttbl{\f0\fnil\fcharset162 Segoe UI;}}
{\colortbl ;\red15\green23\blue42;\red37\green99\blue235;\red100\green116\blue139;}
\viewkind4\uc1 
\pard\sa200\sl276\slmult1\b\f0\fs24\cf1 NEXMOTE YAZILIM L\'ddSANS VE KULLANIM S\'d6ZLE\'deMES\'dd\par
\b0\fs18\cf3 S\'fcr\'fcm 1.0 - Kurumsal ve Bireysel Kullan\'fdm\par
\cf0\fs20\par
\b 1. Lisans Hakk\'fd ve Kapsam\'fd\b0\par
NexMote yaz\'fdl\'fdm\'fd ("Yaz\'fdl\'fdm"), uzaktan bilgisayar y\'f6netimi, canl\'fd masa\'fcst\'fc deste\'f0i ve telemetri izleme ama\'e7lar\'fdyla geli\'fetirilmi\'fetir. Bu lisans, yaz\'fdl\'fdm\'fd kurma, \'e7al\'fd\'fet\'fdrma ve kullanma hakk\'fd tan\'fdr.\par
\par
\b 2. G\'fcvenlik ve Gizlilik\b0\par
NexMote t\'fcm ileti\'feim oturumlar\'fdnda TLS 1.3 ve u\'e7tan uca yetkilendirme standartlar\'fdn\'fd uygular. Uzak eri\'feim yetkileri sistem y\'f6neticisinin ve kullan\'fdc\'fdn\'fdn onay\'fdna ba\'f0l\'fdd\'fdr.\par
\par
\b 3. Kullan\'fdm Sorumlulu\'f0u\b0\par
Kullan\'fdc\'fd, bu yaz\'fdl\'fdm\'fd yaln\'fdzca yetkili oldu\'f0u cihazlar \'fczerinde kullanmay\'fd, yetkisiz eri\'feim veya k\'f6t\'fcye kullan\'fdm ger\'e7ekle\'fetirmemeyi kabul eder.\par
\par
\b 4. Destek ve G\'fcncellemeler\b0\par
G\'fcncellemeler ve destek i\'e7in \cf2\b https://nexmote.com\cf0\b0  adresini ziyaret edebilirsiniz.\par
}
"@

$licensePath = [System.IO.Path]::Combine($installerAssetsDir, "license.rtf")
[System.IO.File]::WriteAllText($licensePath, $licenseRtf, [System.Text.Encoding]::ASCII)
Write-Host "Created: $licensePath"
