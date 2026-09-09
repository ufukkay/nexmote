# 📜 NexMote - Geliştirme Kuralları, Ajan Ana Yasası & Mühendislik Standartları

Bu doküman, **NexMote** projesini geliştiren tüm mühendisler, yazılımcılar ve Yapay Zeka (AI) ajanları için hazırlanmış **Bağlayıcı Kurallar, Geliştirme Araçları ve Ajan Ana Yasası Kılavuzudur**. Yapılacak her hata düzeltmesi, yeni özellik veya derlemede bu dokümandaki ilkelere harfiyen uyulması zorunludur.

---

## 🏛️ DEĞİŞTİRİLEMEZ AJAN ANA YASASI (10 TEMEL İLKE)

### 📌 Madde 1: Otomatik ve Kesintisiz Başlama (Zero-Touch Boot)
- **Kural:** Bilgisayar açıldığında, yeniden başlatıldığında veya `NexMote-Agent-Setup.msi` kurulumu bittiği anda ajan **hiçbir kullanıcı müdahalesi olmadan** derhal arka planda devreye girecektir.
- **Teknik Güvenceler:**
  1. **Windows Servisi (`NexMote.Agent.Windows`):** `LocalSystem` yetkisiyle `Start="auto"` olarak çalışır. Çökme durumunda Windows Service Recovery 0 saniye gecikmeyle servisi yeniden ayağa kaldırır.
  2. **Oturum Gözlemcisi (Watchdog):** Servis içindeki `RunSessionWatchdogAsync` gözlemcisi 1 saniyede bir aktif kullanıcı oturumunu denetler; oturum açıldığı an `NexMote.Agent.Tray.exe --tray` sürecini doğrudan kullanıcının masaüstüne enjekte eder (`CreateProcessAsUser` + `winsta0\default`).
  3. **Kayıt Defteri Çift Emniyeti:** `HKLM\Software\Microsoft\Windows\CurrentVersion\Run` altında `NexMoteAgentTray` anahtarı bulunur.
  4. **Kurulum Anında Başlama:** WiX MSI paketi `ServiceControl Id="ServiceControl" Start="install"` ile kurulumun son adımında servisi anında ayağa kaldırır.
  5. **Doğrudan Başlama (Tiksiz / Onaysız Kurulum):** MSI kurulum paketinde kullanıcıya "Uygulamayı şimdi başlat" gibi hiçbir onay kutusu (checkbox / tik işareti) gösterilmeyecektir. Kurulum tamamlandığı anda Ajan servisi ve masaüstü tepsisi hiçbir ek onay veya tıklama beklenmeksizin doğrudan ve otomatik olarak devreye girecektir.

### 📌 Madde 2: Her Zaman En Üst Düzey Yetki (Domain Admin / SYSTEM Eşdeğeri)
- **Kural:** Bilgisayarda oturum açmış kullanıcı ister kısıtlı bir standart kullanıcı, ister Active Directory Domain kullanıcısı veya misafir olsun; Ajan ve uzaktan çalıştırılan tüm komutlar **her zaman `NT AUTHORITY\SYSTEM` (makinenin en üst düzey çekirdek yetkisi)** ile çalışacaktır.
- **Teknik Güvenceler:**
  1. **UIPI (Kullanıcı Arayüzü Ayrıcalık Yalıtımı) Aşımı:** Windows'ta standart kullanıcı oturumundaki süreçlerin yönetici pencerelerine tıklaması engellidir. NexMote, aktif oturuma SYSTEM yetkili Girdi Yardımcısı (`NexMote.Agent.Tray.exe --input-helper`) enjekte eder.
  2. **Domain Kullanıcı İzni (`BuildPipeSecurity`):** Girdi Yardımcısının Named Pipe (`NexMoteInputHelper_{sessionId}`) ACL kurallarına `AuthenticatedUserSid` ve `WorldSid` tanımlıdır; domain kullanıcılarının SYSTEM yardımcısına fare/klavye sinyali göndermesi garanti edilir.
  3. **UAC Engeli Olmaksızın Tıklama:** Windows UAC (`consent.exe`), Görev Yöneticisi, Kayıt Defteri ve "Yönetici Olarak Çalıştır" pencerelerine teknisyen fare ve klavyesiyle serbestçe müdahale edebilir.
  4. **Uzak Terminal Yetkisi:** Web panelinden çalıştırılan tüm CMD/PowerShell komutları Windows Servisi üzerinden doğrudan `NT AUTHORITY\SYSTEM` ayrıcalığıyla koşar.

### 📌 Madde 3: Windows Çekirdek/Servis Seviyesinde Çalışma (Kullanıcı Bağımsızlığı)
- **Kural:** Ajan asla belirli bir kullanıcının profiline (`AppData`, `Roaming` vb.) bağımlı olmayacak, `%ProgramFiles%\NexMote` dizininde Windows Servisi olarak bağımsız yaşayacaktır.
- **Teknik Güvenceler:**
  1. **Oturum Yokken Bile Çevrimiçi:** Bilgisayarda henüz kimse oturum açmamış olsa dahi (Windows Giriş/Kilit ekranı / Winlogon), Ajan sunucuda "Çevrimiçi" görünür, telemetri gönderir ve uzaktan erişilebilir.
  2. **Winlogon Canlı Yayıncısı (`--system-session`):** Oturum açık değilken veya kilitliyken Windows Servisi `NexMote.Agent.Tray.exe --system-session` sürecini `winsta0\Winlogon` masaüstünde başlatır; teknisyen giriş ekranını canlı izleyebilir ve yönetebilir.
  3. **Kullanıcı Değiştirme (Switch User):** Bir kullanıcı oturumu kapattığında veya kullanıcı değiştirdiğinde Ajan asla kapanmaz, sunucu bağlantısı kopmaz.

### 📌 Madde 4: %100 Evrensel UTF-8 Uyumluluğu (Zero-Mojibake)
- **Kural:** Terminal komutları, dosya adları, donanım/yazılım envanteri, hata günlükleri ve Türkçe karakterler (`ç, ğ, ı, ö, ş, ü, İ, Ğ`) tüm katmanlarda **UTF-8** olarak işlenecektir.
- **Teknik Güvenceler:**
  1. **Konsol Kodlaması:** CMD ve PowerShell süreçlerinde `chcp 65001` ve `Console.OutputEncoding = UTF8` kullanılır.
  2. **JSON Serileştirme:** Ajan, Teknisyen ve API arasındaki tüm JSON paketleri UTF-8 (BOM'suz) ile serileştirilir.
  3. **Bozuk Karakter Yasağı:** Web konsolunda, veritabanında veya uzak masaüstünde hiçbir zaman soru işareti (`?`) veya bozuk karakter (``) gösterilemez.

### 📌 Madde 5: Sessiz ve Kesintisiz Uzaktan Güncelleme (OTA Self-Update)
- **Kural:** Ajanın yeni bir sürümü yayınlandığında veya web panelinden "Güncelle" emri verildiğinde, son kullanıcıya hiçbir onay penceresi fırlatmadan **arka planda sessizce (`/qn`) güncellenecektir.**
- **Teknik Güvenceler:**
  1. **Açılışta Denetim:** Ajan her açılışında 3-4 saniye sonra sessizce `/api/updates/check` sorgusu yapar.
  2. **Sessiz İndirme ve Kurulum:** Yeni sürüm algılandığında MSI paketi `%ProgramData%\NexMote\Agent\pending-update.msi` konumuna indirilir ve Windows Servisi tarafından `msiexec.exe /i ... /qn` ile LocalSystem yetkisiyle kurulur.
  3. **Kimlik Koruma:** Güncelleme sırasında cihaz kimliği (`identity.json`), kayıt anahtarları ve veritabanı eşleşmesi asla kaybolmaz.

### 📌 Madde 6: Sessiz ve Şeffaf Arayüz & Kurulum (Zero-Distraction Installer & Tray)
- **Kural:** Ajan açıldığında kullanıcının karşısına aniden büyük pencereler, formlar fırlatmayacaktır. MSI kurulum paketinde gereksiz büyük görseller, lisans sözleşmesi onay ekranları, karmaşık metinler veya adımlar yer almayacaktır.
- **Teknik Güvenceler:**
  1. `NexMote.Agent.Tray.exe` varsayılan olarak `openDashboardOnStart = false` ile açılır; doğrudan sağ alt köşedeki Bildirim Alanında (Tray) yeşil kalkan simgesiyle sessizce yerini alır.
  2. Durum Paneli (`DashboardForm`) yalnızca kullanıcı simgeye çift tıkladığında veya Başlat menüsü kısayoluna bilerek bastığında açılır.
  3. **Görsel ve Metin Sadeliği (Minimal MSI):** Kurulum paketinde gereksiz büyük görseller (dialog/banner bitmap'leri) veya lisans sözleşmesi kabul ekranları bulunmaz; kurulum tek tıkla/sessizce ve doğrudan tamamlanır.

### 📌 Madde 7: Ağ ve Bağlantı Dayanıklılığı (Never Give Up)
- **Kural:** İnternet kopsa, modem yeniden başlasa, sunucu bakıma girse veya IP değişse bile Ajan asla pes etmeyecek ve kapanmayacaktır.
- **Teknik Güvenceler:**
  1. **Sonsuz Yeniden Bağlanma (`InfiniteRetryPolicy`):** SignalR bağlantısı 0s, 2s, 5s, 10s aralıklarla sonsuza kadar yeniden bağlanmayı dener.
  2. **Anında Yeniden Senkronizasyon:** Ağ veya sunucu erişilebilir olduğu milisaniyede cihaz web panelinde anında "Çevrimiçi" durumuna geçer.

### 📌 Madde 8: Kilit Ekranı ve Yazılımsal SAS (Ctrl+Alt+Del) Desteği
- **Kural:** Teknisyen, kilitli veya parola ekranındaki bir bilgisayara uzaktan bağlandığında fiziksel klavyeye gerek kalmadan uzaktan `Ctrl+Alt+Del` gönderebilmelidir.
- **Teknik Güvenceler:**
  1. Windows Servisi başlangıçta `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System` altında `SoftwareSASGeneration = 3` ve `PromptOnSecureDesktop = 0` politikalarını otomatik uygular.
  2. Teknisyen konsolundaki "Ctrl+Alt+Del Gönder" butonu SYSTEM yetkili Girdi Yardımcısına `send-sas` sinyali göndererek Winlogon ekranını güvenle açar.

### 📌 Madde 9: Cihaz Kimliği Bütünlüğü ve Kendi Kendini Onarma (Self-Healing Identity)
- **Kural:** Cihazın adı, etki alanı veya IP adresi değişse bile sistemde **asla mükerrer (çift) cihaz kaydı oluşmayacaktır.**
- **Teknik Güvenceler:**
  1. Cihaz kimliği (`DeviceId` ve `AgentToken`) `%ProgramData%\NexMote\identity.json` dosyasında saklanır ve korunur.
  2. Çöken veya kullanıcı tarafından Görev Yöneticisinden sonlandırılan yardımcı süreçler (Tray veya InputHelper), Windows Servisi Watchdog'u tarafından 1 saniye içinde otomatik yeniden ayağa kaldırılır.

### 📌 Madde 10: Kurumsal Denetim İzi ve Uçtan Uca Güvenlik (Audit & TLS 1.3)
- **Kural:** Teknisyenin cihaz üzerinde gerçekleştirdiği her işlem denetlenebilir ve şifreli olmalıdır.
- **Teknik Güvenceler:**
  1. Tüm canlı ekran akışı, girdi ve komut sinyalleri TLS 1.3 / WSS ile şifrelenir.
  2. Uzaktan çalıştırılan tüm CMD ve PowerShell komutları, kimin çalıştırdığı, tarih ve çıktı özetiyle birlikte sunucudaki `CommandAudits` tablosuna kaydedilir.

---

## 🛠️ GELİŞTİRME ARAÇLARI VE OTOMASYON BETİKLERİ

NexMote projesinde derleme, paketleme ve dağıtım işlemleri için aşağıdaki standart betikler ve araçlar kullanılır:

| Betik / Komut | Görevi | Örnek Kullanım |
| :--- | :--- | :--- |
| **`scripts/deploy-iis.ps1`** | Web ön yüzünü Vite ile derler, API'yi publish eder, `nexmote.db`'yi ezmeden IIS sunucusuna aktarır ve `/health` kontrolü yapar. | `.\scripts\deploy-iis.ps1`<br>`.\scripts\deploy-iis.ps1 -SkipWebBuild` |
| **`scripts/package-windows.ps1`** | WiX v5 ile kurumsal per-machine MSI paketlerini (`Agent`, `Technician`, `Cleaner`, `Deployer`) derler, SHA-256 hesaplar ve `versions.json` üretir. | `.\scripts\package-windows.ps1 -Version "0.7.4" -SkipCodeSigning` |
| **`scripts/deploy-server.ps1`** | Linux (Ubuntu VPS) sunuculara rsync/ssh ile dağıtım yapar. | `.\scripts\deploy-server.ps1 -TargetHost "186.241.21.133"` |
| **Birim Testleri (`dotnet test`)** | Proje içi .NET 8 SDK ile ağ, deep-link ve kontrat testlerini çalıştırır. | `& ".\.dotnet\dotnet.exe" test -c Release` |
| **Web Frontend Build** | React 18 + TypeScript + Vite konsolunu derler. | `cd web; npm run build` |

---

## 🧪 HER DÜZELTMEDE 7 ADIMLI DOĞRULAMA KONTROL LİSTESİ

Herhangi bir kod değişikliği, hata düzeltmesi veya yeni özellik eklendiğinde sırasıyla şu adımlar tamamlanmalıdır:

1. [ ] **Derleme Kontrolü:** `dotnet build NexMote.sln -c Release` hatasız ve uyarısız tamamlanmalıdır.
2. [ ] **Birim Testleri:** `& ".\.dotnet\dotnet.exe" test -c Release` ile tüm testlerin (26/26) yeşil olduğu doğrulanmalıdır.
3. [ ] **Versiyon Tutarlılığı:** Yeni sürüm numarası tüm `.csproj` dosyalarında, `web/package.json`, `package-windows.ps1`, `CHANGELOG.md` ve `AGENTS.md` içinde eşitlenmelidir.
4. [ ] **MSI Paketleme:** `scripts/package-windows.ps1` çalıştırılarak yeni `.msi` dosyaları ve `downloads/versions.json` üretilmelidir.
5. [ ] **Veritabanı Güvenliği:** Dağıtım sırasında canlı `nexmote.db`, `nexmote.db-wal` ve `dpkeys/` dosyalarının kesinlikle ezilmediği teyit edilmelidir.
6. [ ] **Sağlık Denetimi:** `https://nexmote.com/health` uç noktasından `{"product":"NexMote","status":"ok"}` yanıtı alınmalıdır.
7. [ ] **UAC & Girdi Testi:** Teknisyen uygulamasından canlı oturum açılıp hem standart pencerelerde hem de UAC / Yönetici onay ekranlarında farenin ve klavyenin çalıştığı doğrulanmalıdır.

---

## 🚫 MARKA VE İSİM POLİTİKASI
- **Katı Kural:** GitHub deposunda, `README.md`, `CHANGELOG.md`, `RULES.md`, dokümantasyonlarda, commit mesajlarında ve kod yorumlarında **AnyDesk, RustDesk, TeamViewer** gibi 3. taraf firma ve ürün adları kesinlikle **geçirilmeyecektir**. Tüm özellikler ve mimari yalnızca **NexMote**'un kendi özgün kurumsal kimliği ile tanımlanacaktır.
