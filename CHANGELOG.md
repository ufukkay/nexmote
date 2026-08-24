# 📋 NexMote - Sürüm Günlüğü ve Değişiklik Tarihçesi (Changelog)

Bu doküman, **NexMote** projesinde yayınlanan her sürümdeki yeni özellikleri, hata düzeltmelerini, performans iyileştirmelerini ve mimari değişiklikleri detaylı olarak kayıt altına alır.

---

## 🏷️ [v0.6.9] - 2026-08-24 (Güncel Canlı Sürüm)
### 🚀 Yeni Özellikler
- **Bağlantı Onayı (Consent) ve Granüler Ajan İzinleri:** Güvenlik profillerine, teknisyen bağlanmadan önce hedef kullanıcıdan onay istenmesini sağlayan bir onay modu (`Kısıtsız` / `Her Zaman Sor` / `Kullanıcı Aktifse Sor`, zaman aşımlı onay diyaloğu) ve dört ayrı izin bayrağı (`Sadece İzleme`, `Uzak Terminal`, `Pano`, `Dosya Aktarımı`) eklendi. Onay bekleniyorken hedef ekranda "Teknisyen Bağlı" rozeti (`ShowConnectionBanner`) gösterilir; teknisyen tarafında bekleme/red durumları canlı olarak state olarak yansıtılır.
- **Teknisyen ↔ Ajan Pano (Kopyala/Yapıştır) Senkronizasyonu:** Teknisyen uygulamasında elle "📋 Pano" gönderme butonu eklendi; ayrıca canlı oturum sırasında Ajan kendi yerel pano değişikliğini otomatik algılayıp Teknisyene iletir, Teknisyen de gelen metni otomatik kendi panosuna yazar (iki yönlü, `AllowClipboard` güvenlik profili bayrağıyla kapatılabilir).
- **İndirme Merkezinde Gruba Özel Tek Script ile Kurulum:** "Hedef grup" seçilip indirilen paket artık seçilen gruba/profile gerçekten uygun kuruluyor — yeni "Tek Script ile Kur" (.ps1) MSI'ı sunucudan indirir, sessizce kurar ve ajanı doğrudan seçilen gruba bağlar; ayrı bir kurulum-sonrası provizyon adımı gerekmez (eski "sadece provizyon script'i" seçeneği zaten kurulu ajanlar için korundu).
### 🛠️ Hata Düzeltmeleri
- **Ajan Sunucuya Bağlanamıyor Görünüyordu:** Kök neden yanlış anlaşılıyordu — sunucu/ağ sorunu değil, daha önce web panelinden silinmiş bir cihazın (`DeletedDevices` tablosu) otomatik yeniden kaydının kasıtlı olarak engellenmesiydi (`DeviceRegistry.Enroll`). Etkilenen cihaz kaydı üretimde temizlendi.
- **Uzaktan Ajan Güncellemesi Süresiz Kilitleniyordu (kritik):** `/api/agents/{id}/update` ile tetiklenen sessiz güncelleme, Ajan Windows Servisinin kendi `msiexec /i ... /qn` çağrısında canlı ortamda **süresiz kilitleniyordu** (msiexec süreçleri 40+ dakika "Responding" ama ilerlemesiz kaldı) — kök neden, kurulumu başlatan sürecin (servisin) aynı zamanda Windows Installer'ın yerleşik Restart Manager'ı tarafından da ayrıca kapatılmaya/yeniden başlatılmaya çalışılmasıydı (WiX'in kendi `ServiceControl`/`KillAgentTrayProcess` adımlarıyla çakışan bir yarış durumu). Daha da kötüsü: kilitlenen `pending-update.msi` silinmediği için **her servis yeniden başlatmasında/heartbeat'te aynı kilitlenme tekrarlanıyordu** (kendi kendini durduran bir döngü). `MSIRESTARTMANAGERCONTROL=Disable` bayrağı eklenip zaten çalışan bir `msiexec` varsa yeni kurulumun tetiklenmesi engellendi (`Worker.CheckPendingUpdate`). Üretimde etkilenen cihaz (DESKTOP-SIH3FAC) elle kurulumla kurtarıldı.
- **Web Konsolu Kenar Çubuğunda Bayat Sürüm Etiketi:** `App.tsx`'teki sabitlenmiş `v0.6.3 Pro` metni hiçbir zaman güncellenmiyordu (0.6.4-0.6.8 sürümleri boyunca yanlış gösterim) — artık gerçek sürümü yansıtıyor.
- **Eksik Veritabanı Kolonu Migration'ı:** `SecurityProfileEntity`'ye eklenen 8 yeni bağlantı onayı/izin kolonu için `Program.cs`'teki manuel `ALTER TABLE` bloğu unutulmuştu — bu haliyle canlıya alınsaydı mevcut `SecurityProfiles` tablosunda "no such column" hatasıyla tüm güvenlik profili sorguları kırılırdı. Aynı `ALTER TABLE ... try/catch` deseniyle tamamlandı.

---

## 🏷️ [v0.6.3] - 2026-08-19
### 🚀 Yeni Özellikler
- **Web Üzerinden Uzaktan Sessiz Ajan Kaldırma (Remote Self-Uninstall):**
  - Web konsolundan bir cihaz veya birden çok cihaz silinirken açılan onay modalında `🛡️ Hedef Bilgisayardaki NexMote Ajanını da Kaldır (Sessiz Uninstall)` seçeneği eklendi (çevrimiçi cihazlar için varsayılan olarak aktif).
  - Silme işlemi tetiklendiğinde `DELETE /api/devices/{id}?uninstallAgent=true` ile hedef cihaza SignalR üzerinden `RemoteUninstallRequested` sinyali iletilir.
  - Cihazdaki Windows Servisi (`LocalSystem`), paketle birlikte gelen `NexMote.Cleaner.exe` derin temizleyicisini `%TEMP%` üzerinden sessiz modda (`--silent --from-temp`) devreye sokarak Windows Servisini, Tepsi uygulamasını, Program Files dosyalarını ve Kayıt Defteri girdilerini tamamen kaldırır.
- **Web Üzerinden Doğrudan CMD ve PowerShell Terminali:**
  - Teknisyen masaüstü uygulaması açmaya veya cihaza canlı bağlanmaya gerek kalmadan, doğrudan web konsolundan (**[https://nexmote.com](https://nexmote.com)**) komut çalıştırma desteği.
  - CMD (`cmd.exe`) ve PowerShell (`powershell.exe`) olmak üzere iki ayrı kabuk sekmesi.
  - Windows Servisi (`LocalSystem`) üzerinden %100 sessiz, UAC onaysız tam yönetici (`NT AUTHORITY\SYSTEM`) çalıştırma.
  - Hızlı komut butonları (`ipconfig`, `whoami`, `netstat`, `Get-Service`, `Get-Process`, vb.), komut geçmişi (Yukarı/Aşağı yön tuşları), çıktı kopyalama ve temizleme.

### 🛠️ Hata Düzeltmeleri & İyileştirmeler
- **Windows Açılışında ve Yeniden Başlatmada Tray Simgesi:**
  - Windows Servis katmanında `TryLaunchInActiveSessionAsUser` için `SeAssignPrimaryTokenPrivilege`, `SeIncreaseQuotaPrivilege` ve `SeTcbPrivilege` süreç ayrıcalık etkinleştirmesi eklendi.
  - Kullanıcı token'ı henüz hazır değilse SYSTEM oturumuna otomatik fallback mekanizması kuruldu; cihaz yeniden başladığında bildirim alanında sağ altta tepsi simgesinin gelmesi garanti altına alındı.
- **Masaüstü ve Başlat Menüsü Kısayollarından Durum Paneli Açılışı:**
  - Masaüstü kısayoluna `Arguments="--dashboard"` eklendi.
  - Uygulama başlangıcında `WindowsFormsSynchronizationContext` garantiye alındı; `SW_RESTORE` ve `SetForegroundWindow` Win32 API'leri ile açık olan formun öne gelmesi sağlandı.
  - Ajan doğrudan çift tıklandığında veya kısayoldan açıldığında antivirüs tarzı Durum Panelinin (`DashboardForm`) ekrana gelmesi sağlandı.
- **Yüklü Uygulamalar Envanteri & Temiz Kullanıcı Adı:**
  - 64-bit ve 32-bit Registry Uninstall kayıtları taranarak web konsolunda yüklü programlar sekmesi eklendi.
  - Domain/UPN ekleri temizlenerek gerçek oturum açan veya son oturum açan kullanıcı adı gösterimi sağlandı.

---

## 🏷️ [v0.6.2] - 2026-08-19
### 🚀 Yeni Özellikler & İyileştirmeler
- **Uzaktan Yeniden Başlatma Sonrası Otomatik Yeniden Bağlanma (Reboot Recovery Watchdog):**
  - Teknisyen uygulamasında Güç -> "Yeniden Başlat" / "Güvenli Mod" seçildiğinde bağlantı koptuğunda oturum kapanmaz; bekleme durumuna geçer.
  - Arka planda sunucudan cihazın açılması izlenir; cihaz açılıp çevrimiçi olduğu saniye canlı masaüstü oturumu **otomatik olarak** yeniden başlatılır.
- **Kilit & Windows Giriş (Winlogon) Ekranında Klavye-Fare Desteği:**
  - `DesktopHelper.AttachToActiveDesktop()` mantığı `MAXIMUM_ALLOWED` ve `Winlogon` masaüstü erişimiyle güçlendirildi.
  - `InputInjector` içine çift katmanlı enjeksiyon eklendi (`SendInput` başarısız olduğunda `mouse_event` ve `keybd_event` sürücü katmanına geri düşüş).
- **Ajan Güncelleme İlerleme Penceresi:**
  - Ajan arayüzünde "Ajanı Güncelle" butonuna tıklandığında anlık indirme hızını ve aşamalarını gösteren `UpdateProgressForm` formu eklendi.
- **URL Başına "www." Koyma Zorunluluğunun Kaldırılması:**
  - Teknisyen ve Ajan ayarlarında girilen sunucu adresleri `NexMoteHttp.NormalizeUrl` ile otomatik olarak standart URL formatına (`https://...`) dönüştürülecek şekilde normalize edildi.

---

## 🏷️ [v0.6.1] - 2026-08-18
### 🚀 Yeni Özellikler & İyileştirmeler
- **Admin Kimlik Doğrulama & Endpoint Güvenliği:**
  - Backend API'ye `POST /api/auth/login` ve `AdminAuthFilter` eklendi. Cihaz listesi ve sunucu ayarları Bearer Token ile koruma altına alındı.
- **Gerçek Donanım Telemetrisi:**
  - Sahte CPU hesaplamaları kaldırılarak `GetSystemTimes` üzerinden 10 dakikalık kayan pencere ortalamalı gerçek CPU kullanımı (`CpuUsageSampler.cs`) ve `GlobalMemoryStatusEx` ile gerçek RAM kullanımı sağlandı.
- **Sıfır Gecikmeli Adaptif Akış Motoru (4 Kademe):**
  - Ekran değişmediğinde 0 FPS / 0 KB/s uyku modu; fare/ekran hareketinde anında 30+ FPS canlı akış modu.
- **WiX MSI & Inno Setup Çift Paketleme:**
  - Kurumsal per-machine `.msi` ve 1.5 saniyelik ultra hızlı `.exe` kurulum paketleri geliştirildi.

---

## 🏷️ [v0.6.0] - 2026-08-15
### 🚀 İlk Temel Mimari
- **Çoklu Monitör Eş Zamanlı Yayın:** Tüm fiziksel ekranların bağımsız JPEG kareleri halinde WebSocket/SignalR üzerinden canlı aktarımı.
- **Windows Arka Plan Servisi (`NexMote.Agent.Windows`):** LocalSystem ayrıcalığı ile 20s heartbeat, uzaktan komut çalıştırma ve UAC izinleri.
- **Teknisyen Masaüstü Uygulaması (`NexMote.TechnicianApp`):** WPF .NET 8 modern SaaS arayüzü, çoklu ekran gösterimi, uzaktan terminal ve kalite profilleri.
- **Web Konsolu (`web/`):** React 18 + TypeScript + Vite modern cihaz yönetim arayüzü.
