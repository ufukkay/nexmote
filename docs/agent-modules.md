# NexMote Agent Modul Plani

## Core Service

- Windows Service olarak calisir.
- Sunucuya outbound HTTPS/SignalR baglantisi kurar.
- Heartbeat, policy ve komut kuyrugunu yonetir.
- Kullanici oturumu kapansa bile calismaya devam eder.

## Session Helper

- Aktif kullanici session'inda calisir.
- Ekran yakalama, clipboard ve input modullerine ev sahipligi yapar.
- Core Service ile named pipe uzerinden konusur.

## Remote Shell

- CMD/PowerShell komutlarini rol bazli yetkiyle calistirir.
- Komut, cikti, exit code ve calistiran teknisyen audit log'a yazilir.
- Serbest komut yetkisi sadece ileri rollerde acilir.

## Device Control

- Ilk fazda USB storage policy ac/kapat.
- Ileride vendor/product/device instance ID bazli allowlist.
- Driver seviyesinde device control sonraki fazda ayrica degerlendirilecek.

## Uninstall Protection

- MSI uninstall server tarafindan uretilen tek kullanimlik token ister.
- Offline break-glass kodu ayrica uretilir.
- Kaldirma denemeleri loglanir.

## Branding

- Urun adi, tray icon, logo, destek bilgisi ve sunucu URL'si installer config ile gelir.
- Config imzali olacak ve agent tarafinda dogrulanacak.

## UAC / Secure Desktop

- Core Service kontrol kanalini korur.
- Session degisimleri izlenir.
- Helper yeni aktif session icin yeniden baslatilir.
- Credential saklama yoktur; teknisyen girdisi audit kurallariyla yonetilir.

