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

**Durum (2026-08-09):** Temel calistirma ve audit kismi implemente edildi —
`NexMote.Agent.Tray` `remote-command` sinyalini alip `CommandRunner` ile
CMD/PowerShell calistiriyor, sonucu `command-result` sinyaliyle geri donduruyor
ve `POST /api/audit/commands` ile backend'e (SQLite `CommandAudits` tablosu)
yaziyor. Rol bazli yetkilendirme henuz yok (bkz. `security-model.md` "Su anki
durum") — anahtari bilen her teknisyen serbest komut calistirabilir; bu, rol
modeli implemente edilene kadar bilinen bir sinirlamadir.

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

