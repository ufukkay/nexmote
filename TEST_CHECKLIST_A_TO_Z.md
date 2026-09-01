# 🛡️ NexMote Uçtan Uca (A'dan Z'ye) Cihaz Test Kontrol Listesi

Bu doküman, **NexMote v0.7.0** sisteminin tüm istemci, sunucu, web paneli ve teknisyen özelliklerini gerçek cihazınız (**`DESKTOP-SIH3FAC`**) üzerinde adım adım test edebilmeniz için hazırlanmış **Detaylı Doğrulama ve Test Kılavuzudur**.

---

## 📌 Hızlı Erişim Bilgileri
- **Web Teknisyen Konsolu:** [https://nexmote.com](https://nexmote.com)
- **Teknisyen Masaüstü Kurulumu:** [https://nexmote.com/downloads/NexMote-Technician-Setup.msi](https://nexmote.com/downloads/NexMote-Technician-Setup.msi)
- **Ajan Kurulum Paketi:** [https://nexmote.com/downloads/NexMote-Agent-Setup.msi](https://nexmote.com/downloads/NexMote-Agent-Setup.msi)
- **Tam Temizlik Aracı:** [https://nexmote.com/downloads/NexMote-Cleanup-Setup.msi](https://nexmote.com/downloads/NexMote-Cleanup-Setup.msi)

---

## 📋 Test Aşamaları & Kontrol Listesi

### 🟢 Aşama 1: Windows Yeniden Başlatma & Otomatik Devreye Girme (Ana Yasa Madde 1 & 2)
> Bilgisayarınızı yeniden başlattıktan sonra test edilecek ilk adımdır.

- [ ] **1.1 Sessiz Başlangıç:** Windows açıldığında ekrana aniden hiçbir büyük form veya dikkat dağıtıcı pencere fırlamamalıdır.
- [ ] **1.2 Tepsi Simgesi:** Sağ alt köşedeki Sistem Tepsisinde (Notification Area) yeşil kalkanlı **NexMote** simgesi kendiliğinden hazır olarak belirmelidir.
- [ ] **1.3 Windows Servis Kontrolü:** `Görev Yöneticisi -> Hizmetler` altında `NexMoteService` durumunun `Çalışıyor (Running)` olduğu görülmelidir.

---

### 🟢 Aşama 2: Sadeleştirilmiş Ajan Durum Paneli & Tepsi Menüsü
- [ ] **2.1 Sağ Tık Menü Sadeleşmesi:** Tepsi simgesine sağ tıklandığında yalnızca 2 seçenek görünmelidir:
  - `🛡️ Durum Paneli`
  - `🚪 Çıkış`
- [ ] **2.2 Kompakt Durum Paneli:** Tepsi simgesine çift tıklandığında (veya sağ tıklayıp `🛡️ Durum Paneli` dendiğinde):
  - Form boyutu şık ve kompakt (500x360) açılmalıdır.
  - Üstte `NexMote Agent v0.7.0` ve sağda yeşil `• Ajan Aktif` rozeti olmalıdır.
  - Ortada yeşil kalkan ikonuyla **"Uzaktan Destek Hizmeti Hazır"** görünmelidir.
  - **Bilgisayar Adı:** `DESKTOP-SIH3FAC` ve **Aktif Kullanıcı:** `ufuk.kaya` doğru şekilde yazmalıdır.
- [ ] **2.3 Canlı Otomatik Tazeleme (Bug Kontrolü):** Ajan sağ tık menüsünden kapatılıp masaüstü kısayolundan tekrar açıldığında, "Bağlantı Kuruluyor..." ekranında takılmadan 1 saniye içinde yeşil **"Uzaktan Destek Hizmeti Hazır"** durumuna kendiliğinden geçmelidir.

---

### 🟢 Aşama 3: Web Konsolu Canlı Telemetri & Donanım Envanteri
- [ ] **3.1 Cihaz Listesi:** [https://nexmote.com](https://nexmote.com) açıldığında `DESKTOP-SIH3FAC` cihazı yeşil **Çevrimiçi** rozetiyle listelenmelidir.
- [ ] **3.2 Gerçek CPU & RAM Ölçümü:** Cihaz detayına tıklandığında:
  - Gerçek CPU kullanım çubuğu (örn: `%4 - %18`) dinamik olarak görünmelidir.
  - Gerçek fiziksel RAM (örn: `8.2 GB / 16.0 GB`) ve boş disk alanı (GB) listelenmelidir.
- [ ] **3.3 Yüklü Uygulamalar Listesi:** **Performans / Envanter** sekmesine geçildiğinde bilgisayarınızdaki kurulu programların güncel listesi gelmelidir.

---

### 🟢 Aşama 4: Web Uzak Terminal (CMD & PowerShell) & Uygulama Kaldırma
- [ ] **4.1 CMD Komut Testi:** Web konsolundaki **Terminal** sekmesine gidip:
  - `whoami` veya `ipconfig` yazıp **Çalıştır** butonuna basın.
  - Siyah terminal ekranına 1-2 saniyede komut çıktısı eksiksiz akmalıdır.
- [ ] **4.2 PowerShell Komut Testi:**
  - PowerShell modunu seçip `Get-Process | Select-Object -First 5` komutunu çalıştırın.
  - Kısıtlama hatası almadan çıktının düzgün Türkçe karakterlerle geldiğini doğrulayın.
- [ ] **4.3 Aktivite Logu Denetimi:** **Aktivite** sekmesine geçin; çalıştırdığınız komutun zamanı, süresi ve `Exit Code: 0 (Başarılı)` bilgisi loglanmış olmalıdır.

---

### 🟢 Aşama 5: Canlı Uzaktan Masaüstü Bağlantısı (Remote Desktop)
- [ ] **5.1 Tek Tıkla Bağlantı:** Web panelindeki **"Bağlan"** butonuna basın.
- [ ] **5.2 Teknisyen Uygulaması Açılışı:** Tarayıcınız `nexmote://` bağlantısını tetikleyerek **NexMote Technician** masaüstü uygulamasını otomatik olarak açmalıdır.
- [ ] **5.3 Gecikmesiz Görüntü Akışı:** Teknisyen penceresinde masaüstünüz sıfır gecikmeyle canlı olarak görünmelidir.
- [ ] **5.4 Banner Temizliği (Bug Kontrolü):** Hedef bilgisayarın ekranında takılı kalan eski `NexMote: Teknisyen Bağlı` üst popup banner'ı **kesinlikle çıkmamalıdır**.

---

### 🟢 Aşama 6: Giriş Kontrolleri, Kalite Modları & Çoklu Ekran
- [ ] **6.1 Fare & Klavye Kontrolü:** Teknisyen ekranından fareyi hareket ettirin, pencerelere tıklayın ve metin yazın; uzak ekranda gecikmesiz işlenmelidir.
- [ ] **6.2 Kalite Profilleri:** Üst ada araç çubuğundaki **⚡ Kalite** butonuna basarak:
  - `Hız Modu` (Yüksek FPS)
  - `Dengeli Mod` (Varsayılan)
  - `Kristal Netlik` (Tam netlik)
  modları arasında geçiş yapıldığında görüntü kalitesinin değiştiğini test edin.
- [ ] **6.3 Sadeleştirilmiş Araç Çubuğu Kontrolü:** Üst ada çubuğunda gereksiz butonların (`Komut`, `Pano`, `Kilit Aç`, `Güç`) kalktığı; yalnızca `Monitör`, `Görünüm`, `⚡ Kalite`, `Yenile` ve `Sonlandır` butonlarının kaldığı doğrulanmalıdır.
- [ ] **6.4 Güvenli Sonlandırma:** **Sonlandır** butonuna basarak bağlantıyı kapatın; oturum başarıyla sonlanmalıdır.

---

### 🟢 Aşama 7: Otomatik Güncelleme & Canlılık Denetimi (Ana Yasa Madde 4)
- [ ] **7.1 Açılış Güncelleme Kontrolü:** Hem Ajan hem Teknisyen açılırken `/api/updates/check` adresini sessizce sorgulamalıdır.
- [ ] **7.2 Kalıcı Loglama:** Veritabanında oturum süreleri ve komut denetim kayıtlarının eksiksiz saklandığı doğrulanmalıdır.

---

## 🎯 Test Sonuç Tablosu

| Test Aşaması | Test Edilen Modül | Durum | Notlar |
| :--- | :--- | :---: | :--- |
| **Aşama 1** | Windows Açılışı & Servis | ⏳ Bekliyor | |
| **Aşama 2** | Ajan Tepsi & Durum Paneli | ⏳ Bekliyor | |
| **Aşama 3** | Web Telemetri & Donanım | ⏳ Bekliyor | |
| **Aşama 4** | Web Terminal (CMD/PS) | ⏳ Bekliyor | |
| **Aşama 5** | Canlı Masaüstü Akışı | ⏳ Bekliyor | |
| **Aşama 6** | Fare/Klavye & Kalite | ⏳ Bekliyor | |
| **Aşama 7** | Güncelleme & Kararlılık | ⏳ Bekliyor | |

---

> 💡 **Hazır:** Şimdi bilgisayarınızı yeniden başlatıp teste başlayabilirsiniz. Döndüğünüzde her adımı birlikte gözden geçirip doğrulayabiliriz!
