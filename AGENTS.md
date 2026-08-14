# NexMote - Yapay Zeka (AI Agent) Master Geliştirme Rehberi & Derin Proje Mimarisi

Bu doküman, **NexMote** projesini geliştiren, inceleyen veya projenin herhangi bir modülüne kod ekleyen tüm Yapay Zeka (AI) ajanları (Antigravity, Cursor, Claude Code, GitHub Copilot, Windsurf vb.) için hazırlanmış **Kapsamlı Master Rehber ve Teknik Mimarı Dokümanıdır**.

---

## 📌 Proje Özeti & Vizyon

**NexMote**, kurumsal düzeyde uzaktan bilgisayar yönetimi, canlı masaüstü izleme/kontrolü, uzak terminal komut çalıştırma ve istemci destek platformudur (AnyDesk, RustDesk ve TeamViewer alternatifi).

- **Canlı Sistem URL:** [https://nexmote.com](https://nexmote.com)
- **Sunucu IP Adresi:** `72.62.198.100` (Hostinger Ubuntu 24.04 LTS VPS)
- **Sağlık Endpoint:** `https://nexmote.com/health` -> `{"product":"NexMote","status":"ok"}`
- **Erişim Dokümanı:** [docs/server-credentials.md](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/docs/server-credentials.md) (git'te takip edilmiyor, sadece yerel)
- **Güncel Client Sürümü:** `0.4.0` (bkz. [Versiyonlama](#-versiyonlama--otomatik-güncelleme-mimarisi))

---

## 🧱 Klasör Yapısı ve Modül Sorumlulukları

```
NexMote/
├── AGENTS.md                 # Master AI Geliştirici ve Proje Mimarı Dokümanı
├── NexMote.sln               # Ana Visual Studio Solution Dosyası
├── backend/                  # ASP.NET Core 8 Web API & SignalR Sunucusu
│   └── src/NexMote.Api/
│       ├── Auth/              # AdminAuthFilter.cs (Bearer token korumalı endpoint filtresi)
│       ├── Data/               # AppDbContext (Entity Framework Core SQLite)
│       ├── Hubs/               # SignalingHub.cs (/hubs/signaling - WebSocket Canlı Akış)
│       ├── Services/           # DeviceRegistry, RemoteSessionRegistry, DownloadCatalog
│       ├── wwwroot/            # Üretilen React Web Ön Yüzü Statik Dosyaları (Vite dist)
│       ├── Program.cs          # Uygulama Başlangıcı, static files, CORS, SignalR, admin auth grubu & Route Haritası
│       ├── appsettings.json    # Dev konfigürasyonu (dev-* varsayılan sırlar içerir, production'da kullanılmaz)
│       └── appsettings.Production.json # Prod konfigürasyonu — GERÇEK SIRLARI İÇERMEZ, hepsi systemd env var'dan gelir
├── web/                       # React 18 + TypeScript + Vite Web Teknisyen Konsolu
│   └── src/
│       ├── App.tsx            # Ana UI Bileşeni (Login, Cihaz Listesi, Donanım Metrikleri, Terminal, İndirmeler)
│       ├── api.ts             # REST API Fetch Kontratları, DTO Tipleri, admin token yönetimi
│       ├── main.tsx           # React DOM Başlangıç Noktası
│       └── styles.css         # Vanilla CSS SaaS Tasarım Sistemi (Glassmorphism, Light Theme, CSS Variables)
├── agent-windows/             # Hedef Windows Bilgisayarlarda Çalışan İstemci Servisleri
│   ├── src/NexMote.Agent.Windows/  # Windows Background Service (LocalSystem, 20s Heartbeat, gerçek CPU telemetrisi,
│   │                                #  self-update kurulumu, input-helper oturum enjeksiyonu, UAC secure-desktop ayarı)
│   └── src/NexMote.Agent.Tray/     # Kullanıcı oturumunda çalışan Tray uygulaması: antivirüs tarzı durum paneli,
│                                    #  çoklu ekran eş zamanlı yayın, otomatik kalite, uzak komut/UAC yükseltme,
│                                    #  --input-helper modu (SYSTEM yetkili girdi enjeksiyonu, UAC'a tıklamak için)
├── technician-app/            # Teknisyen Masaüstü Uygulaması (WPF .NET 8, web konsoluyla aynı açık SaaS teması)
│   └── src/NexMote.TechnicianApp/  # nexmote:// Protokol Tetiklemeli, çoklu ekran yan yana canlı izleme,
│                                    #  gerçek admin login + Yönetici Olarak Çalıştır (UAC) desteği
├── shared/                    # Ortak Veri Tipleri & Kontrat Kütüphanesi
│   └── NexMote.Shared/        # DTO'lar, Protocol Mesajları, Cihaz Bilgileri (Contracts/*.cs)
├── docs/                      # Sistem Dokümantasyonları ve Güvenlik
│   ├── server-credentials.md  # VPS IP, Root Şifresi, SSH Anahtar Bilgileri, Admin/Enrollment sırlarının okunma yolu
│   ├── architecture.md        # Sistem Mimarisi ve Veri Akış Şeması
│   └── security-model.md      # Güvenlik ve Yetkilendirme Modeli
├── scripts/
│   ├── package-windows.ps1    # Agent+Technician'ı publish edip MSI'ları üretir, downloads/versions.json yazar
│   └── build-msi.ps1          # WiX v4 ile .wxs üretip MSI derler (Version parametreli)
└── infra/                     # Nginx ve Systemd Yayına Alma Dokümanları
```

---

## 📊 Veritabanı Mimarisi (EF Core & SQLite)

SQLite veritabanı sunucuda `/var/www/nexmote/nexmote.db` yolunda saklanır.

### 1. `Devices` Tablosu (Kayıtlı İstemci Cihazlar)
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | Guid (PK) | Benzersiz Cihaz Kimliği |
| `DeviceName` | string | Bilgisayar Adı |
| `DomainName` | string | Çalışma Grubu / Domain |
| `OperatingSystem` | string | İşletim Sistemi Sürümü |
| `AgentVersion` | string | Yüklü Agent Sürümü — **her heartbeat'te güncellenir** (sadece enrollment anında değil), gerçek çalışan assembly versiyonundan okunur |
| `ActiveUser` | string | ⚠️ Bilinen kusur: bu alan LocalSystem olarak çalışan Windows Servisi'nin heartbeat kodundan geliyor, bu yüzden gerçek oturum açmış kullanıcı yerine genelde makine hesabını (`DOMAIN\MAKINE$`) gösterir |
| `IpAddress` | string | Yerel / Dış IP Adresi |
| `LocationCode` | string | Lokasyon Kodu (Örn: OFFICE, LAB) |
| `CpuUsagePercent` | double | **Gerçek** CPU kullanımı — `GetSystemTimes` ile ölçülür, 10 dakikalık kayan pencere ortalaması (`CpuUsageSampler.cs`); RAM'den türetilen eski sahte hesaplama kaldırıldı |
| `MemoryTotalMb` / `MemoryUsedMb` | long | Gerçek fiziksel RAM (`GlobalMemoryStatusEx`) |
| `DiskFreeMb` | long | Boş Disk Alanı (MB) |
| `UptimeSeconds` | long | Sistem Çalışma Süresi (Saniye) |
| `LastSeenAt` | DateTimeOffset | Son Heartbeat / Sinyal Zamanı |
| `EnrolledAt` | DateTimeOffset | Agent Kayıt Zamanı |
| `AgentToken` | string | Cihaza Özel Güvenlik Sinyal Token'ı (heartbeat/audit doğrulaması için) |

> **Not:** `CpuUsagePercent`/`MemoryTotalMb`/`MemoryUsedMb`/`DiskFreeMb` alanları `DeviceSummary` DTO'sunda da bulunur — daha önce backend'de saklanıp API'ye hiç yansıtılmıyordu (web panelinde sabit fallback değerler — %15 CPU, 8/16GB RAM — gösteriliyordu), bu düzeltildi.

### 2. `ServerSettings` Tablosu (Sunucu Ayarları)
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | int (PK) | Ayar ID (Tekli Kayıt) |
| `ServerUrl` | string | Sunucu Bağlantı URL'i (`https://nexmote.com`) |
| `EnrollmentKey` | string | Agent Kayıt Doğrulama Anahtarı — rastgele üretilip rotate edildi, artık `/api/settings` **admin token gerektirir** |
| `HeartbeatSeconds` | int | Heartbeat Gönderim Sıklığı (Varsayılan: 20sn) |
| `DefaultLocationCode` | string | Varsayılan Lokasyon |

---

## 🔐 Admin Kimlik Doğrulama (Backend API)

Backend'de daha önce **hiç kimlik doğrulama yoktu** — `/api/devices`, `/api/settings` gibi endpoint'ler herkese açıktı (gerçek cihaz envanteri, enrollment key dahil). Bu kapatıldı:

- `POST /api/auth/login` — `{email, password}` alır (varsayılan: `admin@nexmote.com` / `admin123`, `Admin:Email`/`Admin:Password` env var ile değiştirilebilir), doğruysa `Admin:ApiKey` değerini token olarak döner.
- Admin-korumalı route grubu (`AdminAuthFilter`, `Authorization: Bearer <token>` ister): `GET/POST /api/settings`, `GET /api/devices`, `GET /api/devices/{id}`, `POST /api/remote-sessions`, `POST /api/agents/{id}/update`.
- Agent'a özel route'lar (kendi token mekanizmalarını kullanır, admin auth'a girmez): `POST /api/agents/enroll` (EnrollmentKey), `POST /api/agents/{id}/heartbeat` ve `POST /api/audit/commands` (cihaza özel AgentToken).
- Herkese açık kalanlar: `GET /health`, `GET /api/downloads`, `GET /downloads/{file}`, `GET /api/updates/check` (agent/technician self-update akışı bunlara auth olmadan erişebilmeli).
- **Sırlar asla repoya işlenmez.** `Admin:ApiKey` ve `Enrollment:Key`, sunucuda `/etc/systemd/system/nexmote.service.d/override.conf` içinde `Environment=` satırları olarak tutulur; gerçek değerleri görmek için sunucuya bağlanıp o dosyayı okumak gerekir (bkz. `docs/server-credentials.md`).
- Web konsolu ve Teknisyen uygulaması, girişten sonra bu token'ı saklayıp (`localStorage` / bellek) her korumalı isteğe `Authorization` header'ı olarak ekliyor. Teknisyen uygulaması "login ekranı yok" kuralını korumak için **sessizce** `admin@nexmote.com`/`admin123` ile giriş yapıp token alıyor; başarısız olursa (şifre değişmişse) login ekranını gösteriyor.

---

## 🌐 REST API Endpoint Kataloğu

| Yöntem | Endpoint | Auth | Açıklama |
| :--- | :--- | :--- | :--- |
| `GET` | `/health` | — | Sunucu durum kontrolü |
| `POST` | `/api/auth/login` | — | Admin e-posta/şifre doğrulayıp Bearer token döner |
| `POST` | `/api/agents/enroll` | EnrollmentKey | Yeni Windows Agent kayıt işlemi |
| `POST` | `/api/agents/{id}/heartbeat` | AgentToken | Agent periyodik telemetri (gerçek CPU/RAM/Disk/AgentVersion dahil) |
| `GET` | `/api/downloads` | — | MSI paket indirme kataloğunu döner |
| `GET` | `/downloads/{fileName}` | — | MSI dosyasını indirir |
| `POST` | `/api/remote-sessions` | **Bearer** | Teknisyen için canlı `nexmote://` deep-link oturumu açar |
| `GET`/`POST` | `/api/settings` | **Bearer** | Sunucu genel konfigürasyonunu okur/günceller |
| `GET` | `/api/devices` | **Bearer** | Kayıtlı cihazların özet ve gerçek donanım metriklerini döner |
| `GET` | `/api/devices/{id}` | **Bearer** | Tekil cihaz detayı |
| `GET` | `/api/updates/check` | — | `downloads/versions.json`'dan okunan gerçek Agent/Technician sürüm ve OTA kataloğu |
| `POST` | `/api/agents/{id}/update` | **Bearer** | Seçili (online) cihaza uzaktan sessiz Agent güncelleme sinyali gönderir |
| `POST` | `/api/audit/commands` | AgentToken | Uzak komut çalıştırma denetim kaydı |

> `POST /api/downloads/generate` kaldırıldı — sunucuda (Linux) PowerShell/WiX olmadığı için hiçbir zaman çalışmıyordu; MSI üretimi artık sadece yerel `scripts/package-windows.ps1` ile yapılıp sunucuya elle/scp ile yükleniyor.

---

## ⚡ SignalR Canlı Sinyalleşme Hub (`/hubs/signaling`)

`SignalingHub`, opak `(sessionId, type, payload)` string mesajlarını ilgili odaya relay eder; JSON şemaları `NexMote.Shared.Contracts` içinde tanımlıdır.

- **Bağlantı Noktası:** `wss://nexmote.com/hubs/signaling`
- **Hub metotları:** `JoinDevice(deviceId, agentToken)`, `JoinDeviceSession(sessionId, deviceId, agentToken)`, `JoinTechnicianSession(sessionId, token)`, `SendSignal(sessionId, type, payload)`.
- **`type` değerleri (SendSignal payload'ları):**
  - `screen-info` — `RemoteScreenInfo` (her ekranın Index/Name/Width/Height/**Left/Top**'ı içerir, çoklu ekran layout'u kurmak için)
  - `screen-frame-multi` — `MultiScreenFrame(DisplayIndex, JpegBase64)`; **her fiziksel ekran eş zamanlı, bağımsız olarak** yayınlanır (eski tekli `screen-frame` + `select-display` modeli kaldırıldı)
  - `remote-input` — `RemoteInputEvent` (artık `DisplayIndex` alanı var; X/Y o ekranın kendi yerel koordinatı)
  - `remote-command` — `RemoteCommandRequest` (artık `RunAsAdmin` alanı var — UAC yükseltmeli çalıştırma)
  - `command-result` — `RemoteCommandResult` (artık `ElevationDenied` alanı var)
  - `ping` / `pong`, `refresh-screen`, `file-chunk`, `clipboard-text`
  - `RemoteUpdateRequested` (ayrı bir hub->client event, payload değil) — Agent'a yeni MSI indirme emri

---

## 💻 Donanım Telemetrisi (`CpuUsageSampler.cs`, `SystemTelemetry.cs`)

1. **CPU:** `CpuUsageSampler` arka planda 15sn'de bir `GetSystemTimes` ile örnekleme yapar, **10 dakikalık kayan pencere ortalamasını** heartbeat'te gönderir. Eski RAM-türevli sahte hesaplama (`GetCpuUsagePercent`) kaldırıldı.
2. **RAM:** `GlobalMemoryStatusEx` ile fiziki RAM toplamı/kullanımı.
3. **Disk:** `DriveInfo` ile sistem sürücüsü boş alanı.
4. **IPv4:** Sanal ağ kartları (Hyper-V, WSL, VMware) filtrelenir, gerçek fiziksel yerel IP tespit edilir. (`NetworkInfo.cs` — kullanılmayan, daha zayıf bir kopyaydı — silindi.)
5. **Web Konsolu:** `App.tsx` Donanım & Performans kartlarında artık backend'den gelen **gerçek** değerleri gösterir; ayrıca cihazın versiyonu `/api/updates/check`'teki en güncel sürümden eskiyse turuncu "Güncelleme mevcut" rozeti çıkar.

---

## 🔢 Versiyonlama & Otomatik Güncelleme Mimarisi

### Versiyon kaynağı
Her üç client projesinin (`NexMote.Agent.Windows`, `NexMote.Agent.Tray`, `NexMote.TechnicianApp`) `.csproj`'unda gerçek bir `<Version>` var; çalışma zamanında `Assembly.GetExecutingAssembly().GetName().Version` ile okunup hem heartbeat'e hem UI'a (teknisyen pencere başlığı, Tray dashboard'u) yansıtılır. `scripts/package-windows.ps1 -Version X.Y.Z` çalıştırıldığında bu değer MSI'ın WiX `Package Version`'ına ve `downloads/versions.json`'a (backend'in `/api/updates/check`'in okuduğu kaynak) yazılır — artık "MSI'daki gerçek versiyon" ile "API'nin duyurduğu versiyon" birbirinden kopuk değil.

### OTA akışı (ÖNEMLİ — bootstrap sırası dikkat gerektirir)
1. **Agent (arka plan, sessiz):** Tray, `RemoteUpdateRequested` sinyalinde MSI'ı indirip `%ProgramData%\NexMote\Agent\pending-update.msi` konumuna bırakır (kendi kurmaya ÇALIŞMAZ). Windows Servisi (LocalSystem, tam yetkili) periyodik olarak bu dosyayı kontrol edip `msiexec /i ... /qn` ile **kendisi** kurar — LocalSystem zaten tam yetkili olduğu için UAC'a hiç gerek yok, tamamen sessiz çalışır.
   - **Neden böyle:** Eskiden Tray, `msiexec`'i doğrudan (kullanıcı yetkisiyle, `runas` olmadan) çağırıyordu; per-machine bir MSI + servis kurulumu admin yetkisi gerektirdiği için bu her zaman **sessizce** başarısız oluyordu (`/qn` hiçbir hata göstermez). Bu, production'da birden fazla cihazın güncellenememesine yol açan kök nedendi.
   - **Bootstrap catch-22:** Bu düzeltme kendi kendini otomatik güncelleyemez — eski (bozuk) koddaki bir agent, düzeltmeyi içeren yeni MSI'ı da aynı şekilde sessizce kuramaz. Eski bir agent'ı bu mekanizmaya geçirmek için **bir kez elle** MSI kurulumu şart; ondan sonraki tüm güncellemeler gerçekten sessiz çalışır.
2. **Teknisyen Self-Updater:** `🚀 Güncelleme Kontrol Et` butonu `/api/updates/check`'i sorgular, versiyonu gerçekten karşılaştırır (eskiden hep "güncelleme var" derdi), ve MSI'ı artık `Verb = "runas"` ile başlatır — kullanıcı zaten ekranın başında olduğu için çıkan UAC'ı hemen onaylayabilir.

### MSI dağıtım notu
Sunucuda **iki** downloads klasörü var (`/var/www/nexmote/downloads` ve `/var/www/nexmote/wwwroot/downloads`) — `DownloadCatalog` hangisini kullanacağını dosya varlığına göre seçer, ama statik dosya sunumu (nginx/Kestrel `UseStaticFiles`) her zaman `wwwroot/downloads`'ı önceliklendirir. **MSI güncellemesi yaparken ikisine de kopyalamak gerekir**, yoksa `/api/downloads` metadata'sı ile gerçek indirilen dosya boyutu tutarsız olur.

---

## 🖥️ Teknisyen Masaüstü Uygulaması (WPF) Kuralları

1. **Doğrudan Açılış (Login Ekranı Yok):** Uygulama açılırken görünür bir login penceresi sormaz; arka planda sessizce `admin@nexmote.com`/`admin123` ile `/api/auth/login`'e istek atıp admin token alır, `https://nexmote.com`'a bağlanır. Sessiz giriş başarısız olursa (şifre değişmişse) login ekranını gösterir.
2. **Local IP Engelleme:** Sunucu bağlantı adreslerinde `192.168...`, `127.0.0.1` veya `http://` tespiti halinde otomatik `https://nexmote.com` adresine zorlama yapılır.
3. **Çoklu Ekran — EŞ ZAMANLI YAN YANA:** Artık "tek ekran seçip geçiş yapma" modeli yok. Hedef cihazda kaç fiziksel ekran varsa, **hepsi aynı anda** yayınlanır ve teknisyen ekranında yan yana ayrı canlı kutular (`MultiScreenPanel`, dinamik `Image` per ekran) olarak gösterilir. Her kutunun kendi fare/klavye haritalaması var (`DisplayIndex` ile agent'a hangi ekranın hedeflendiği bildirilir). Bant genişliği/CPU kullanımı ekran sayısıyla orantılı artar — bilinçli bir tercih.
4. **Otomatik Görüntü Kalitesi:** Manuel kalite butonu kaldırıldı. Agent, her karenin SignalR gönderim süresine bakarak JPEG kalitesini (20-80 aralığında) kendiliğinden ayarlar.
5. **Yönetici Olarak Çalıştır (UAC):** Komut panelinde "🛡️ Yönetici Olarak Çalıştır" kutucuğu işaretlenirse, agent komutu Windows `runas` ile başlatır — hedef cihazda gerçek UAC istemi çıkar. Bunun uzaktan görünüp tıklanabilmesi için agent tarafında iki ek mekanizma gerekiyor (bkz. aşağıki bölüm).
6. **Görüntü Dayanıklılığı:** Donma/kopma durumunda 6 saniyelik Watchdog otomatik yenileme tetikler; `🔄 Ekranı Yenile` ve `F11 Tam Ekran` mevcut.
7. **Sadeleştirilmiş toolbar:** Pano Gönder, Win+D, Alt+Tab, Cihaz Listesi butonları kaldırıldı ("Sonlandır" zaten cihaz listesine dönüyor).

---

## 🛡️ UAC Görünürlüğü ve Uzaktan Tıklanabilirliği (Agent tarafı)

Ekran-yakalama tabanlı hiçbir uzaktan erişim aracı (bizimki dahil), varsayılan Windows davranışında UAC istemine **erişemez** — iki ayrı koruma katmanı var, ikisi de aşıldı:

1. **Secure Desktop izolasyonu** — UAC normalde izole bir "secure desktop"ta açılır, ekran yakalama bile bunu göremez. **Çözüm:** Windows Servisi başlangıçta `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System\PromptOnSecureDesktop = 0` ayarını yapıyor (`Worker.EnsureUacVisibleToRemoteSupport`) — UAC artık normal masaüstünde açılıyor, görülebiliyor.
2. **UIPI (User Interface Privilege Isolation)** — normal (kullanıcı yetkili) bir süreçten gelen simüle fare/klavye girdisi, daha yüksek bütünlük seviyesindeki (UAC) pencerelere Windows tarafından **engellenir**, secure desktop'tan bağımsız ayrı bir katman. **Çözüm:** Windows Servisi, `SessionProcessLauncher.cs` ile (SYSTEM token'ı `SeTcbPrivilege` kullanarak aktif oturuma `SetTokenInformation(TokenSessionId)` + `CreateProcessAsUser` ile etiketleyip) `NexMote.Agent.Tray.exe --input-helper` modunu **SYSTEM yetkisiyle kullanıcının oturumu içinde** başlatıyor. Normal Tray, gelen `remote-input` olaylarını doğrudan enjekte etmek yerine yerel bir named pipe (`NexMoteInputHelper_{sessionId}`) üzerinden bu SYSTEM yardımcısına iletiyor; SYSTEM, UAC penceresinden bile yetkili olduğu için giriş engellenmiyor.
   - **Güvenlik:** Pipe'a bağlanan istemcinin çalıştırılabilir dosya yolu (`GetNamedPipeClientProcessId`), yardımcının kendi yolu ile birebir eşleşmiyorsa komutlar reddedilir — başka bir yerel sürecin bu pipe'ı SYSTEM'e yükselme aracı olarak kötüye kullanmasını engellemek için.
   - Yardımcı süreç ulaşılamıyorsa (henüz başlamamışsa), Tray normal (yükseltmesiz) doğrudan enjeksiyona geri düşer — sıradan uzaktan kontrol bozulmaz.
   - **Durum:** Bu mekanizma standart/belgeli bir Windows tekniğidir ama gerçek hedef makinede (LocalSystem servisi + oturum enjeksiyonu) uçtan uca saha testi gerektirir — geliştirme ortamında simüle edilemez.

---

## 🐚 Antivirüs Tarzı Agent Tray Dashboard'u

Tray simgesine çift tıklandığında (veya menüden "🛡️ Durum Panelini Aç"), sade bir mesaj kutusu yerine `DashboardForm` açılır: kalkan ikonu + renkli durum bandı ("Korunuyor" yeşil / "Bağlantı Bekleniyor" turuncu / "Korumasız" kırmızı — servis durumu + SignalR bağlantı durumuna göre), Sunucu/Servis/Ekran Akışı/Sürüm bilgi satırları, "Web Panelini Aç"/"Sunucu Ayarları"/"Yenile" aksiyon butonları. Web konsoluyla aynı renk paleti (mavi vurgu `#2563EB`, açık yüzeyler) WinForms `FlatStyle` ile uygulanır.

---

## 🎨 Frontend Tasarım Sistemi (Web + Teknisyen Uygulaması, Ortak Dil)

1. **Tasarım Dili:** Modern Light SaaS / Helpdesk stili — hem web konsolu hem Teknisyen WPF uygulaması (uzak masaüstü canlı görüntüleme alanı hariç, orada bilinçli olarak koyu arka plan kullanılır, video-player kanvası gibi).
   - Arka Plan: Soft Slate Off-White (`#f8fafc`)
   - Ana Vurgu Rengi: Canlı Mavi (`#2563eb`)
   - Çevrimiçi Göstergeleri: Zümrüt Yeşili (`#10b981`)
   - Kartlar: Beyaz (`#ffffff`), yumuşak gölgeler (`rgba(0,0,0,0.04)`), cam efektleri (`backdrop-filter: blur(14px)`).
2. **Tipografi:** Kompakt font ölçeği (Taban font boyutu: `12.5px`).
3. **Bildirim Davranışı:**
   - Otomatik 10 saniyelik periyodik cihaz yenilemeleri **sessiz (silent)** çalışır; kullanıcıya pop-up toast gösterilmez.
   - Bildirimler sadece kullanıcı manuel **Yenile** butonuna bastığında veya işlem yaptığında sağ üst zil ikonunun hemen altında çıkar.
4. **Oturum Yönetimi (Login):**
   - Web arayüzü ve Teknisyen uygulaması tekli teknisyen oturumu içerir (`admin@nexmote.com` / `admin123`), artık backend'de **gerçekten** doğrulanıyor (bkz. Admin Kimlik Doğrulama). Oturum token'ı `localStorage`'da saklanır.

---

## 🛠️ Yerel Geliştirme Komutları (Build & Run)

### 1. .NET Çözümünü Derleme
```powershell
.\.dotnet\dotnet.exe build NexMote.sln
```

### 2. Web Ön Yüzü Derleme ve Çalıştırma
```cmd
# Dev Server (http://localhost:5173)
cmd /c "npm --prefix web run dev"

# Production Build (TypeScript Check & Vite Build)
cmd /c "npm --prefix web run build"
```

### 3. Backend Sunucuyu Yerelde Çalıştırma
```powershell
.\.dotnet\dotnet.exe run --project backend/src/NexMote.Api/NexMote.Api.csproj --urls "http://127.0.0.1:5080"
```
Yerel geliştirmede `Admin:ApiKey` = `dev-admin-api-key`, `Enrollment:Key` = `dev-enrollment-key` (appsettings.json'da tanımlı, production'da KULLANILMAZ).

### 4. MSI Paketlerini Yeniden Oluşturma (Agent + Teknisyen)
```powershell
$key = "<mevcut EnrollmentKey — /api/settings'den admin token ile okunur>"
powershell -ExecutionPolicy Bypass -File scripts\package-windows.ps1 `
  -ServerUrl "https://nexmote.com" -EnrollmentKey $key -Version "X.Y.Z" `
  -AgentReleaseNotes "..." -TechnicianReleaseNotes "..."
```
Bu, `downloads/NexMote-Agent-Setup.msi`, `downloads/NexMote-Technician-Setup.msi` ve `downloads/versions.json` üretir.

### 5. Canlı Sunucuya Yayınlama (Deploy to Production)
```powershell
# 1. Linux x64 paketini derle
.\.dotnet\dotnet.exe publish backend/src/NexMote.Api/NexMote.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish-linux

# 2. Web ön yüzünü derle ve kopyala
cmd /c "npm --prefix web run build"
powershell -Command "New-Item -ItemType Directory -Force -Path 'publish-linux\wwwroot'; Copy-Item -Recurse -Force 'web\dist\*' 'publish-linux\wwwroot\'"

# 3. Ziple ve SCP ile sunucuya yükle
powershell -Command "Compress-Archive -Path 'publish-linux\*' -DestinationPath 'publish-linux.zip' -Force; scp -i '$env:USERPROFILE\.ssh\id_ed25519' 'publish-linux.zip' root@72.62.198.100:/tmp/publish-linux.zip"

# 4. Sunucuda aç ve servisi yeniden başlat
powershell -Command "ssh -i '$env:USERPROFILE\.ssh\id_ed25519' root@72.62.198.100 'unzip -o /tmp/publish-linux.zip -d /var/www/nexmote/ && systemctl restart nexmote.service'"

# 5. MSI'ları HER İKİ downloads klasörüne de yükle (bkz. yukarıdaki not)
scp -i "$env:USERPROFILE\.ssh\id_ed25519" downloads\NexMote-Agent-Setup.msi downloads\NexMote-Technician-Setup.msi downloads\versions.json root@72.62.198.100:/var/www/nexmote/wwwroot/downloads/
scp -i "$env:USERPROFILE\.ssh\id_ed25519" downloads\NexMote-Agent-Setup.msi downloads\NexMote-Technician-Setup.msi downloads\versions.json root@72.62.198.100:/var/www/nexmote/downloads/
```

---

## 🌐 Canlı Sunucu (Production) Özeti

- **İşletim Sistemi:** Ubuntu 24.04.4 LTS (Hostinger KVM 1 VPS - `72.62.198.100`)
- **Web Sunucu:** Nginx (`/etc/nginx/sites-available/nexmote`) - Reverse Proxy & WebSocket Headers
- **SSL Sertifikası:** Let's Encrypt Certbot 256-bit SSL (`https://nexmote.com`, `https://www.nexmote.com`, `https://api.nexmote.com`)
- **Servis Yöneticisi:** `systemd` (`nexmote.service` -> `/var/www/nexmote/NexMote.Api.dll --urls http://127.0.0.1:5080`)
  - Sırlar: `/etc/systemd/system/nexmote.service.d/override.conf` (`Admin__ApiKey`, `Enrollment__Key` — repoya işlenmez)
- **Veritabanı:** SQLite (`/var/www/nexmote/nexmote.db`)
- **Kayıtlı test/demo cihazları:** TAL-01888 (aktif test cihazı), DESKTOP-SIH3FAC (gerçek müşteri cihazı — dikkatli olun, test için kullanmayın), 36D6735F-A0A6-4, PC-UFUK

---

## ⚠️ Bilinen Kusurlar / Devam Eden İşler

1. **`ActiveUser` alanı yanlış:** Windows Servisi'nin (LocalSystem) heartbeat kodu `Environment.UserName` okuyor, bu LocalSystem bağlamında gerçek oturum kullanıcısını değil makine hesabını (`DOMAIN\MAKINE$`) döndürüyor. Düzeltme, Tray'in (kullanıcı oturumunda çalışır) bu bilgiyi ayrıca bildirmesini gerektirir.
2. **UAC/SYSTEM input helper saha testi bekliyor:** `SessionProcessLauncher` + `--input-helper` mekanizması standart bir teknik kullanıyor ama gerçek bir hedef makinede uçtan uca doğrulanmadı.
3. **Bootstrap catch-22:** 0.3.2 öncesi bir agent, kendi kendine güncelleme mekanizmasındaki düzeltmeyi de otomatik alamaz — en az bir kez elle MSI kurulumu gerekiyor.

---

*Gelecekte projeye müdahale edecek tüm AI geliştiriciler bu master rehberdeki mimariye, veritabanı şemasına ve tasarım kurallarına bağlı kalmalıdır.*
