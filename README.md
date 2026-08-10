# NexMote

NexMote, `nexmote.com` alan adi icin tasarlanan Windows odakli, self-hosted, agent tabanli kurumsal uzaktan yonetim platformudur.

## Hedef

- IIS uzerinde yayinlanabilen ASP.NET Core backend
- Web tabanli teknisyen paneli
- Windows uzerinde servis olarak calisan C# agent
- Teknisyen bilgisayarinda calisan C# desktop client
- Web panelden `Baglan` butonu ile Technician App'i acma
- Cihaz envanteri, online/offline durumu, oturum baslatma ve audit temeli

## Proje Yapisi

```text
backend/              ASP.NET Core API, SignalR, PostgreSQL hazirligi
web/                  React + TypeScript teknisyen paneli
agent-windows/        C# Windows Service agent
technician-app/       C# WPF teknisyen uygulamasi
shared/               Ortak DTO ve sozlesmeler
installer/            MSI/GPO paketleme notlari
infra/                IIS, database ve deployment notlari
docs/                 Mimari, guvenlik ve yol haritasi
```

## MVP Akisi

1. Agent sunucuya kaydolur.
2. Agent periyodik heartbeat ve cihaz bilgisi gonderir.
3. Teknisyen web panelde cihaz listesini gorur.
4. Teknisyen `Baglan` butonuna basar.
5. Backend gecici remote session olusturur.
6. Tarayici `nexmote://connect?...` linki ile Technician App'i acar.
7. Technician App session bilgisiyle signaling kanalina baglanir.
8. Agent ve Technician App eslestirilir.

## IIS Notu

Backend ASP.NET Core uygulamasi olarak IIS arkasinda calisacak sekilde tasarlanir. IIS uzerinde ASP.NET Core Hosting Bundle gerekir.

## Teknisyen Erisim Anahtari

Backend ilk kez calistiginda otomatik olarak bir "Teknisyen Erisim Anahtari" uretir ve
`nexmote-first-run-credentials.txt` dosyasina (backend'in calisma dizinine) yazar. Web
panele ve Technician App'e giris icin bu anahtar gerekir; `X-Technician-Key` HTTP basligi
olarak gonderilir. Anahtar daha sonra web panelin Sunucu Ayarlari sayfasindan
degistirilebilir. Bu, `/api/devices`, `/api/remote-sessions`, `/api/settings` ve
`/api/downloads/generate` uclarini korur; agent enrollment/heartbeat uclari kendi
enrollment anahtari / agent token mekanizmasini kullanmaya devam eder. Detay icin
`docs/security-model.md`.

## Kurulum Loglari

Kurulum ve servis loglari icin bkz. `docs/troubleshooting-install.md`.
