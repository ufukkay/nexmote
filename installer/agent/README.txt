NexMote Agent test paketi

Kurulum:
1. ZIP dosyasini hedef Windows cihazda bir klasore cikarin.
2. PowerShell'i Yonetici olarak acin.
3. Bu klasorde sunu calistirin:
   powershell -ExecutionPolicy Bypass -File .\install-agent.ps1

Notlar:
- Agent Windows Service olarak "NexMote Agent" adi ile kurulur.
- NexMote Agent Tray kullanici oturumunda sag alt bildirim alaninda gorunur.
- appsettings.json icindeki ServerUrl backend adresini gostermelidir.
- Ayni ag testinde ServerUrl genellikle http://SUNUCU-IP:5080 olmalidir.

Kaldirma:
PowerShell'i Yonetici olarak acip sunu calistirin:
   powershell -ExecutionPolicy Bypass -File .\uninstall-agent.ps1
