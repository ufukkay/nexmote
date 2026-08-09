# Kurulum Loglari

NexMote kurulum ve calisma loglari:

```text
C:\ProgramData\NexMote\Logs
```

## MSI Kurulum Logu

MSI kurulumlari yonetici yetkisi gerektiren per-machine paketlerdir. Kurulum sihirbazi kurulum klasorunu sorar.
Kurulum tamamlandiginda uygulama otomatik baslatilir.
Indirme panelinde Agent ve Technician icin `Turkce` ve `English` MSI varyantlari bulunur. English dosyalari `-en.msi` ekiyle biter.

MSI paketi kurulum sirasinda secilen kurulum klasorune basit bir kurulum logu yazar:

```text
C:\Program Files\NexMote\Agent\install-log.txt
C:\Program Files\NexMote\Technician\install-log.txt
```

Kurulumda farkli klasor secilirse `install-log.txt` o klasorun icinde olusur.

Windows Installer ayrintili loglari arka planda yine kullanicinin temp klasorunde `MSI*.LOG` adi ile olusabilir.

## Kaldirma Dosyasi

Kurulum klasorune kaldirma dosyalari eklenir:

```text
uninstall.cmd
uninstall.ps1
```

`uninstall.cmd` calistirildiginda gerekirse yonetici yetkisi ister ve ilgili MSI paketini kaldirir.

## Agent Runtime Logu

Agent servis baglanti, enrollment ve heartbeat loglari:

```text
C:\ProgramData\NexMote\Logs\agent-service.log
C:\ProgramData\NexMote\Logs\agent-service-startup-error.log
```

## Smart App Control

Gelisim sirasinda sertifika verilmeden uretilen MSI/EXE paketleri imzasizdir. Windows 11 Smart App Control bu dosyalari engelleyebilir; tek uygulama icin istisna ekleme secenegi yoktur.
Uretim paketi icin Windows SDK `signtool.exe` ve guvenilir bir RSA kod imzalama sertifikasi kullanilmalidir:

```powershell
.\scripts\package-msi.ps1 -ServerUrl "https://nexmote.example.com" -SigningCertificate "C:\Keys\nexmote-code-signing.pfx" -CertificatePassword "<sifre>"
```

Paketleme scripti EXE ve MSI dosyalarini SHA-256 ve RFC 3161 zaman damgasi ile imzalar. Smart App Control, guvenilir sertifika otoritesine baglanan imzalari kabul edecek sekilde tasarlanmistir.

## Script Kurulum Loglari

ZIP/script kurulumlari icin:

```text
C:\ProgramData\NexMote\Logs\agent-install-*.log
C:\ProgramData\NexMote\Logs\agent-uninstall-*.log
C:\ProgramData\NexMote\Logs\technician-install-*.log
C:\ProgramData\NexMote\Logs\technician-uninstall-*.log
```

## Ilk Bakilacak Hatalar

- `Return value 3`: MSI logunda asil hata genelde bu satirdan hemen once olur.
- Servis baslamiyorsa: `agent-service.log`.
- Masaustu kisayolu olusmuyorsa: MSI logunda `Shortcut` veya `DesktopFolder`.
- Agent panelde online olmuyorsa: `agent-service.log` icinde ServerUrl, 401, 404 veya connection refused hatasi.
