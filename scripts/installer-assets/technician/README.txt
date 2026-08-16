NexMote Technician App test paketi

Kurulum:
1. ZIP dosyasini teknisyen bilgisayarinda bir klasore cikarin.
2. PowerShell acin.
3. Bu klasorde sunu calistirin:
   powershell -ExecutionPolicy Bypass -File .\install-technician.ps1

Notlar:
- Uygulama kullanici profilinde LocalAppData altina kurulur.
- nexmote:// protokolu HKCU altina kaydedilir.
- Web panelde Baglan butonuna basildiginda NexMote Technician App acilir.

Kaldirma:
   powershell -ExecutionPolicy Bypass -File .\uninstall-technician.ps1

