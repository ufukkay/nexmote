# NexMote - Yapay Zeka (AI Agent) Master Geliştirme Rehberi & Derin Proje Mimarisi

Bu doküman, **NexMote** projesini geliştiren, inceleyen veya projenin herhangi bir modülüne kod ekleyen tüm Yapay Zeka (AI) ajanları (Antigravity, Cursor, Claude Code, GitHub Copilot, Windsurf vb.) için hazırlanmış **Kapsamlı Master Rehber ve Teknik Mimarı Dokümanıdır**.

## 📜 DEĞİŞTİRİLEMEZ AJAN ANA YASASI (4 TEMEL MADDE)

Bu bölüm, **NexMote** projesinin istemci mimarisinde (Ajan, Windows Arka Plan Servisi, Tray Uygulaması ve Teknisyen Konsolu) **asla taviz verilemeyecek, her zaman çalışması zorunlu olan 4 Temel Ana Yasa Maddesini** tanımlar. Proje üzerinde çalışan her Yapay Zeka (AI) geliştirici ve yazılımcı, herhangi bir kod değişikliği veya derleme yapmadan önce bu maddeleri kontrol etmek ve sistemin bu kurallara %100 uyduğunu garanti etmekle yükümlüdür.

### 📌 Madde 1: Ajan Windows Açılır Açılmaz Otomatik Başlayacaktır
- **Kural:** Bilgisayar yeniden başlatıldığında, açıldığında veya herhangi bir kullanıcı oturum açtığında NexMote Ajanı hiçbir kullanıcı müdahalesine gerek kalmadan arka planda anında devreye girmelidir.
- **Teknik Güvenceler (Çift Katmanlı Koruma):**
  1. **Windows Servisi (`NexMote.Agent.Windows`):** `LocalSystem` yetkisiyle `Start="auto"` olarak çalışır. `RunSessionWatchdogAsync` gözlemcisi 1 saniyede bir aktif kullanıcı oturumunu denetler; oturum açıldığı an `NexMote.Agent.Tray.exe --tray` sürecini doğrudan kullanıcının masaüstüne enjekte eder.
  2. **Kayıt Defteri (`HKLM\Software\Microsoft\Windows\CurrentVersion\Run`):** `NexMoteAgentTray` anahtarı ile Windows açılışında tüm kullanıcılar için otomatik başlatma tanımlıdır.

### 📌 Madde 2: Ajan Alt Kısımda (Sistem Tepsisinde / Tray) Simge Olarak Gelecektir
- **Kural:** Ajan başlatıldığında kullanıcının karşısına aniden büyük pencereler, formlar veya dikkat dağıtıcı ekranlar fırlatmayacaktır. Doğrudan sağ alt köşedeki Sistem Tepsisinde (Notification Tray) zarif ve yeşil kalkanlı durum simgesiyle sessizce yerini alacaktır.
- **Teknik Güvenceler:**
  1. `NexMote.Agent.Tray.exe` varsayılan olarak `openDashboardOnStart = false` ile açılır.
  2. Yalnızca kullanıcı tepsi simgesine çift tıkladığında veya Başlat menüsündeki kısayola bilerek bastığında antivirüs tarzı modern Durum Paneli (`DashboardForm`) açılır.

### 📌 Madde 3: Ajan Kurulumu (MSI) Biter Bitmez Otomatik Açılacaktır
- **Kural:** Teknisyen veya son kullanıcı `NexMote-Agent-Setup.msi` paketini kurduğu anda (ister arayüzlü ister `/qn` sessiz kurulum olsun), bilgisayarı yeniden başlatmaya gerek kalmadan servis ve tepsi ajanı hemen çalışmaya başlayacaktır.
- **Teknik Güvenceler:**
  1. WiX MSI paketi `ServiceControl Id="ServiceControl" Start="install"` ile kurulumun son adımında servisi anında ayağa kaldırır.
  2. Servis başladığı saniye aktif oturumu algılayıp tepsi uygulamasını ekrana getirir.
  3. MSI ExitDialog penceresi sonlandığında `LaunchTrayAppExecSequence` ile tepsi uygulaması `--tray` argümanıyla derhal tetiklenir.

### 📌 Madde 4: Her Açılışta Ajan ve Teknisyen Uygulaması Güncelleme Durumunu Kontrol Edecektir
- **Kural:** Hem Ajan (`NexMote.Agent.Tray`) hem de Teknisyen Uygulaması (`NexMote.TechnicianApp`) her açılışında sunucu üzerinden (`/api/updates/check`) en güncel sürümün yayında olup olmadığını kontrol edecektir.
- **Teknik Güvenceler:**
  1. **Ajan:** Başlangıçtan 3-4 saniye sonra sessizce `CheckForAgentUpdatesAsync(isManual: false)` çalıştırır. Yeni sürüm varsa arka planda `%ProgramData%\NexMote\Agent\pending-update.msi` konumuna indirilir ve Windows Servisi tarafından LocalSystem yetkisiyle sessizce kurulur.
  2. **Teknisyen:** Pencere yüklendiği an (`MainWindow_Loaded`) `CheckForUpdatesAsync(isManual: false)` çalıştırır; yeni teknisyen MSI'ı varsa teknisyene tek tıkla güncelleme imkanı sunar.

#### 📋 Doğrulama & Kontrol Listesi
| Madde | Kontrol Noktası | Beklenen Durum |
| :---: | :--- | :--- |
| **1** | Windows Açılışı | Windows yeniden başlatıldığında Ajan otomatik devreye giriyor mu? |
| **2** | Sessiz Tepsi | Ajan açıldığında ekrana popup fırlatmadan sağ altta simge olarak bekliyor mu? |
| **3** | Kurulum Sonrası | MSI kurulumu biter bitmez cihaz web panelinde anında "Çevrimiçi" oluyor mu? |
| **4** | Açılış Güncellemesi | Ajan ve Teknisyen açılırken `/api/updates/check` adresini sorguluyor mu? |

---

### 🚫 Marka ve İsim Politikası
- **Kural:** GitHub deposunda, `README.md`, `CHANGELOG.md`, dokümantasyonlarda, commit mesajlarında ve kod yorumlarında **AnyDesk, RustDesk, TeamViewer** gibi 3. taraf firma ve ürün adları kesinlikle **geçirilmeyecektir**. Tüm özellikler ve mimari yalnızca **NexMote**'un kendi özgün kurumsal kimliği ile tanımlanacaktır.

---

**NexMote**, kurumsal düzeyde uzaktan bilgisayar yönetimi, canlı masaüstü izleme/kontrolü, uzak terminal komut çalıştırma ve istemci destek platformudur.

- **Canlı Sistem URL:** [https://nexmote.com](https://nexmote.com)
- **Sunucu IP Adresi:** `186.241.21.133` (Hostinger Germany - Frankfurt Ubuntu 24.04 LTS VPS)
- **Sağlık Endpoint:** `https://nexmote.com/health` -> `{"product":"NexMote","status":"ok"}`
- **Erişim Dokümanı:** [docs/server-credentials.md](file:///c:/Users/ufuk.kaya/Desktop/Projeler/NexMote/docs/server-credentials.md) (git'te takip edilmiyor, sadece yerel)
- **Güncel Client Sürümü:** `0.6.3` (bkz. [Versiyonlama](#-versiyonlama--otomatik-güncelleme-mimarisi) ve `CHANGELOG.md`)

---

## 🧱 Klasör Yapısı ve Modül Sorumlulukları

```
NexMote/
├── AGENTS.md                 # Master AI Geliştirici, Proje Mimarı & Ana Yasa Dokümanı
├── CHANGELOG.md              # Sürüm Günlüğü, Değişiklik Tarihçesi ve Versiyon Notları
├── README.md                 # Proje Genel Bakış, Hızlı Başlangıç & Canlı Sistem Rehberi
├── NexMote.sln               # Ana Visual Studio Solution Dosyası (Tüm src/ projelerini içerir)
├── src/                      # Tüm .NET 8 Kaynak Kodları (Tek Çatı Altında)
│   ├── NexMote.Api/          # ASP.NET Core 8 Web API & SignalR Sunucusu
│   │   ├── Auth/             # AdminAuthFilter.cs (Bearer token korumalı endpoint filtresi)
│   │   ├── Data/             # AppDbContext (Entity Framework Core SQLite)
│   │   ├── Hubs/             # SignalingHub.cs (/hubs/signaling - WebSocket Canlı Akış)
│   │   ├── Services/         # DeviceRegistry, RemoteSessionRegistry, DownloadCatalog, SignalSessionAccess
│   │   ├── wwwroot/          # Üretilen React Web Ön Yüzü Statik Dosyaları (Vite dist)
│   │   ├── Program.cs        # Uygulama Başlangıcı, static files, CORS, SignalR, admin auth grubu & Route Haritası
│   │   ├── appsettings.json  # Dev konfigürasyonu
│   │   └── appsettings.Production.json # Prod konfigürasyonu
│   ├── NexMote.Agent.Windows/# Windows Background Service (LocalSystem, 20s Heartbeat, gerçek CPU telemetrisi,
│   │                         #  self-update kurulumu, input-helper oturum enjeksiyonu, UAC secure-desktop ayarı)
│   │                         #  AgentConfiguration.cs, SystemTelemetry.cs, Worker.cs, SessionProcessLauncher.cs
│   ├── NexMote.Agent.Tray/   # Kullanıcı oturumunda çalışan Tray uygulaması: antivirüs tarzı durum paneli,
│   │                         #  çoklu ekran eş zamanlı yayın, sıfır gecikmeli akıllı adaptif motor (4 kademe),
│   │                         #  --input-helper modu (SYSTEM yetkili girdi enjeksiyonu, UAC'a tıklamak için)
│   ├── NexMote.TechnicianApp/# Teknisyen Masaüstü Uygulaması (WPF .NET 8, web konsoluyla aynı açık SaaS teması,
│   │                         #  canlı çoklu monitör, kalite profili seçici [Oto/Hız/Dengeli/Kristal], uzak terminal)
│   └── NexMote.Shared/       # Ortak Veri Tipleri & Konsolide Kontrat Kütüphanesi
│       ├── Contracts/        # AuthContracts.cs, AgentContracts.cs, SessionContracts.cs, StreamingContracts.cs
│       └── Network/          # NexMoteHttp.cs (DNS gecikme korumalı soket yöneticisi)
├── web/                      # React 18 + TypeScript + Vite Web Teknisyen Konsolu
│   └── src/
│       ├── App.tsx           # Ana UI Bileşeni (Login, Cihaz Listesi, Detay Paneli [Genel Bakış/Performans/Terminal/Aktivite sekmeleri], İndirmeler)
│       ├── api.ts            # REST API Fetch Kontratları, DTO Tipleri, admin token yönetimi
│       ├── main.tsx          # React DOM Başlangıç Noktası
│       └── styles.css        # Vanilla CSS SaaS Tasarım Sistemi (Glassmorphism, Light Theme, CSS Variables)
├── scripts/
│   ├── package-windows.ps1   # Agent+Technician+Cleaner'ı publish edip build-msi.ps1'i çağırır
│   └── build-msi.ps1         # WiX v5 ile kurumsal per-machine .msi (Agent/Technician/Cleaner) derleme betiği
├── assets/                   # Uygulama İkonları (nexmote.ico) + installer/ (otomatik üretilen dialog/banner/license)
└── downloads/                # Üretilen Dağıtım Paketleri (MSI) ve versions.json
```

---

## 🏗️ Genel Mimari ve Veri Akış Şeması

```
                            [ Web Teknisyen Konsolu ] (React 18 + TS + Vite)
                                       │
                                       ▼  (REST API: /api/devices, /api/remote-sessions)
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               NEXMOTE BACKEND SUNUCUSU                                │
│                   (ASP.NET Core 8 Minimal API + SignalR + SQLite)                      │
│                                                                                        │
│   ├── REST Endpoints: Auth, Cihaz Kayıt (Enroll), Heartbeat, Settings, OTA Updates     │
│   ├── SignalR Hub (/hubs/signaling): Çoklu ekran karesi, girdi, komut ve dosya rölesi │
│   └── Veritabanı (nexmote.db): Devices, RemoteSessions, ServerSettings, CommandAudits   │
└───────────────────────┬────────────────────────────────────────┬───────────────────────┘
                        │                                        │
     (WebSocket / WSS)  │                                        │  (WebSocket / WSS & nexmote://)
                        ▼                                        ▼
┌───────────────────────────────────────────────┐ ┌──────────────────────────────────────┐
│            HEDEF WINDOWS İSTEMCİSİ            │ │      TEKNİSYEN MASAÜSTÜ UYGULAMASI   │
│                                               │ │            (WPF .NET 8)              │
│  ┌─────────────────────────────────────────┐  │ │                                      │
│  │ Windows Background Service (LocalSystem)│  │ │  ├── Canlı Oturum (Multi-Screen)     │
│  │ ├── 20s Heartbeat & Gerçek CPU Sampler  │  │ │  ├── Fare & Klavye Koordinat Haritası│
│  │ ├── UAC & SAS Registry Ayarları         │  │ │  ├── Uzak Shell (CMD/PowerShell)     │
│  │ ├── Pending MSI/EXE Otomatik Kurulumu   │  │ │  ├── Kalite Modu (Oto/Hız/Dengeli)   │
│  │ └── 1s Session Watchdog & Launcher      │  │ │  └── Güç Yönetimi & Yeniden Başlatma │
│  └────────────────────┬────────────────────┘  │ └──────────────────────────────────────┘
│                       │ (CreateProcessAsUser  │
│                       │  + SeTcbPrivilege)    │
│  ┌────────────────────▼────────────────────┐  │
│  │ Aktif Oturum Süreçleri (User Session)   │  │
│  │ ├── NexMote.Agent.Tray.exe (--tray)     │  │
│  │ │   ├── Tray Icon & Dashboard Paneli    │  │
│  │ │   ├── Çoklu Ekran GDI+ Yakalama       │  │
│  │ │   └── 4 Kademeli Adaptif Akış Motoru  │  │
│  │ └── NexMote.Agent.Tray.exe              │  │
│  │     (--input-helper / SYSTEM Yetkili)   │  │
│  │     └── Named Pipe Girdi Enjektörü      │  │
│  │         (UAC Onay Tıklamaları İçin)     │  │
│  └─────────────────────────────────────────┘  │
└───────────────────────────────────────────────┘
```

---

## 🔒 Güvenlik & Yetkilendirme Modeli

1. **Uçtan Uca İletişim Güvenliği:** Tüm iletişim HTTPS / WSS (TLS 1.3) üzerinden şifrelenir.
2. **Kimlik Doğrulama Katmanları:**
   - **Admin / Teknisyen:** `POST /api/auth/login` ile Bearer Token alır. Tüm yönetimsel endpoint'ler (`/api/devices`, `/api/settings`, `/api/remote-sessions`, `/api/agents/*/update`) `AdminAuthFilter` ile sıkı şekilde korunur.
   - **Agent Kayıt (Enrollment):** Sunucuda belirlenen gizli `EnrollmentKey` gerektirir.
   - **Agent Canlılık (Heartbeat & Audit):** Kayıt anında sunucu tarafından üretilen 32 baytlık cihaza özel `AgentToken` ile doğrulanır.
3. **UAC ve Yönetici Yetkileri:**
   - Uzak komutlarda "Yönetici Olarak Çalıştır" seçildiğinde komut doğrudan Windows `runas` ile yükseltilir.
   - SYSTEM yetkili `--input-helper` modülü, Windows `SoftwareSASGeneration` ve `PromptOnSecureDesktop=0` ile UAC pencerelerine teknisyenin güvenle tıklayabilmesini sağlar.
4. **Denetim (Audit Logging):** Çalıştırılan tüm CMD ve PowerShell komutları, çıkış kodları, standart hata ve çıktı özetleriyle birlikte veritabanında `CommandAudits` tablosuna kaydedilir.

SQLite veritabanı sunucuda `/var/www/nexmote/nexmote.db` yolunda saklanır.

### 1. `Devices` Tablosu (Kayıtlı İstemci Cihazlar)
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | Guid (PK) | Benzersiz Cihaz Kimliği |
| `DeviceName` | string | Bilgisayar Adı |
| `DomainName` | string | Çalışma Grubu / Domain |
| `OperatingSystem` | string | İşletim Sistemi Sürümü |
| `AgentVersion` | string | Yüklü Agent Sürümü — **her heartbeat'te güncellenir** (sadece enrollment anında değil), gerçek çalışan assembly versiyonundan okunur |
| `ActiveUser` | string | LocalSystem servisi, `SessionProcessLauncher.GetActiveSessionUserName()` ile aktif konsol oturumunu `WTSQuerySessionInformation` (WTSUserName/WTSDomainName) üzerinden doğrudan sorgular — gerçek oturum kullanıcısını (`DOMAIN\kullanici`) döner. Oturumda kimse giriş yapmamışsa (kilit/giriş ekranı) makine hesabına (`DOMAIN\MAKINE$`) geri düşer. |
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

## 🔐 Çoklu Kullanıcı Kimlik Doğrulama, Roller ve MFA (Backend API)

**2026-08-23'te köklü değişiklik:** Eskiden tek bir paylaşılan admin kimliği (`Admin:Email`/`Admin:Password`) ve herkese aynı statik `Admin:ApiKey` Bearer token'ı dönen bir model vardı — kim giriş yaptığı, ne zaman, hangi işlemi yaptığı hiç bilinmiyordu. Artık gerçek, kişiye özel çoklu kullanıcı (Admin + Teknisyen rolleri), opak DB-backed oturum token'ları ve TOTP tabanlı MFA var:

- **Kullanıcılar (`Users` tablosu):** Her kullanıcının kendi e-postası, PBKDF2 şifre hash'i (`PasswordHasher<UserEntity>`, `Microsoft.Extensions.Identity.Core`), rolü (`Admin` | `Technician`), aktif/pasif durumu ve isteğe bağlı MFA'sı vardır. İlk açılışta `Users` tablosu boşsa, eski `Admin:Email`/`Admin:Password` config değerlerinden **tek bir Admin kullanıcısı** seed edilir (`UserAuthService.EnsureBootstrapAdmin`, `Program.cs`) — production geçişi koptan olmaz.
- **Giriş akışı (2 adım):** `POST /api/auth/login` (email+şifre) → kullanıcının MFA'sı kapalıysa direkt oturum token'ı, açıksa `{requiresMfa:true, challengeToken}` döner. `POST /api/auth/mfa/verify` (challengeToken + 6 haneli TOTP kodu veya kurtarma kodu) → gerçek oturum token'ı.
- **Oturum token'ları (`UserSessions` tablosu):** JWT değil, opak rastgele token — DB'de sadece SHA-256 hash'i tutulur (`SessionTokens.Hash`), anında iptal edilebilir (örn. bir kullanıcı devre dışı bırakıldığında `UserAuthService.SetActive` tüm aktif oturumlarını `RevokedAt` ile hemen iptal eder). `SessionTokenAuthHandler` (`Auth/`), eski statik `AdminAuthFilter`'ın yerini alan gerçek ASP.NET Core `AuthenticationHandler`'dır; `Bearer <token>` → kullanıcı + rol claim'li `ClaimsPrincipal` üretir.
- **Yetkilendirme politikaları:** `"AnyUser"` (giriş yapmış herkes — Admin veya Teknisyen: cihaz görüntüleme, uzak oturum, komut çalıştırma, uygulama kaldırma, agent güncelleme, server-metrics) ve `"Admin"` (sadece Admin: `/api/settings`, kullanıcı yönetimi, cihaz silme, denetim logu). `Program.cs`'te `admin`/`authed` iki ayrı `MapGroup("/api").RequireAuthorization(...)` grubu olarak uygulanır.
- **MFA (TOTP):** `Otp.NET` ile RFC 6238; secret'lar DB'de ASP.NET Core Data Protection (`IDataProtectionProvider`, anahtarlar `dpkeys/` dizininde diske kalıcı — **restart sonrası kaybolmasın diye asla in-memory bırakılmaz**) ile şifreli tutulur. Kurulum: `POST /api/account/mfa/setup` (secret + `otpauth://` URI, QR web'de client-side `qrcode` npm paketiyle render edilir) → `POST /api/account/mfa/enable` (ilk kodla onay, 10 tek kullanımlık kurtarma kodu bir kereliğine döner) → `POST /api/account/mfa/disable`. MFA hiçbir rol için zorunlu değildir, kullanıcı kendi tercihiyle açar.
- **Kullanıcı yönetimi (Admin-only, `/api/admin/users*`):** listeleme, yeni Admin/Teknisyen oluşturma (geçici şifre üretir), rol değiştirme, devre dışı bırakma/etkinleştirme, kilitlenen bir kullanıcının MFA'sını admin adına zorla sıfırlama.
- **Denetim logu (`ActivityLogs` tablosu, `GET /api/admin/audit-log`, Admin-only):** login başarı/başarısızlık, MFA challenge/başarısız, kullanıcı oluşturma/rol değişikliği/devre dışı bırakma, MFA aç/kapat gibi tüm insan-kullanıcı eylemlerini kaydeder — mevcut `CommandAudits` (cihazda çalıştırılan komutlar) tablosundan **ayrı** bir tablodur.
- Agent'a özel route'lar (kendi token mekanizmalarını kullanır, insan kullanıcı auth'una hiç girmez): `POST /api/agents/enroll` (EnrollmentKey), `POST /api/agents/{id}/heartbeat` ve `POST /api/audit/commands` (cihaza özel AgentToken).
- Herkese açık kalanlar: `GET /health`, `GET /api/downloads`, `GET /downloads/{file}`, `GET /api/updates/check` (agent/technician self-update akışı bunlara auth olmadan erişebilmeli).
- **Sırlar asla repoya işlenmez.** `Enrollment:Key` ve bootstrap `Admin:Password`, sunucuda `/etc/systemd/system/nexmote.service.d/override.conf` içinde `Environment=` satırları olarak tutulur (bkz. `docs/server-credentials.md`). `Admin:ApiKey` **artık kullanılmıyor**, config'ten kaldırıldı.
- **EF Core / SQLite tuzağı:** SQLite provider'ı `DateTimeOffset` kolonlarını ne `ORDER BY`'da ne de bazı bileşik `WHERE` ifadelerinde SQL'e çeviremiyor ("could not be translated" / "does not support expressions of type 'DateTimeOffset' in ORDER BY"). Bu yüzden oturum token doğrulaması önce `TokenHash` ile tek satır çekip geri kalan koşulları (`ExpiresAt`, `RevokedAt` vb.) C# tarafında kontrol ediyor; denetim logu sıralaması da `DeviceRegistry.List()` ile aynı desende önce `.ToList()` sonra client-side `OrderByDescending` yapıyor. Yeni bir DateTimeOffset alanına göre filtre/sıralama eklerken bu deseni takip edin, yoksa runtime'da 500 alırsınız.
- **Kendi kendini kilitleme koruması:** `UserAuthService.SetActive`/`SetRole`, bir kullanıcının **kendi hesabını** devre dışı bırakmasını veya kendi rolünü Admin'den düşürmesini sunucu tarafında engeller (web UI'da da ilgili satırın kontrolleri devre dışı bırakılır). 2026-08-23'te canlıda gerçekten yaşanan bir kilitlenme olayından sonra eklendi — Kullanıcı Yönetimi tablosunda kendi satırı için ayırt edici bir koruma yoktu.

---

## 📧 SMTP E-posta Gönderimi ve E-posta ile Kullanıcı Daveti

Yeni kullanıcı oluştururken artık iki seçenek var: eski "tek seferlik geçici şifre" akışı (`POST /api/admin/users`) hâlâ duruyor, yanında **e-posta ile davet** akışı eklendi (`POST /api/admin/users/invite`) — davet edilen kişi kendi şifresini belirliyor, admin şifre iletmek zorunda kalmıyor.

- **SMTP config (`ServerSettings` tablosu):** Host/Port/Username/FromAddress/FromName + Data Protection ile şifrelenmiş `SmtpPasswordEncrypted` (MFA secret'larıyla aynı desen, ayrı bir `IDataProtector` purpose string'i: `"NexMote.Api.SmtpPassword.v1"`). `GET /api/settings` yanıtında şifre **asla** dönmez (write-only) — web formunda "boş bırakılırsa mevcut şifre korunur" davranışı bu yüzden var.
- **Gönderim:** `EmailService` (`Services/EmailService.cs`), `MailKit` (`SecureSocketOptions.Auto` — hem 465/implicit-SSL hem 587/STARTTLS'i otomatik halleder; eski `System.Net.Mail.SmtpClient` 465'te güvenilir değil, bu yüzden tercih edilmedi).
- **Davet mekanizması:** Davet edilen kullanıcı **hemen** `Users` tablosuna eklenir (`IsActive=true`) ama `PasswordHash`'i 32-byte kriptografik rastgele bir değerin hash'idir — kimse bilmez/tahmin edemez, davet kabul edilene kadar hesap fiilen "kilitli"dir. Ayrı bir `UserInvites` tablosu (token hash, 7 gün geçerlilik, `AcceptedAt`) davet durumunu izler. Aynı (henüz kabul edilmemiş) e-postaya tekrar davet göndermek eski token'ı geçersiz kılıp yenisini üretir (yeniden gönderim).
- **Kabul akışı:** `GET /api/invite/{token}` (public, önizleme) → `POST /api/invite/{token}/accept` (public, `{password}`) → şifreyi ayarlar, daveti "kullanılmış" işaretler, **otomatik oturum açar** (davet kabul eden kişi doğrudan uygulamaya girmiş olur). Web tarafında `App.tsx`, mount olduğunda `window.location.pathname`'in `/invite/` ile başlayıp başlamadığına bakıp ayrı bir "Hesabınızı Etkinleştirin" ekranı gösterir — bu kontrol bir kez hesaplanıp saklanır (`useMemo`), bu yüzden kabul başarılı olduktan sonra normal uygulamaya geçiş ayrı bir `inviteAccepted` state'i ile tetiklenir (URL'i `history.replaceState` ile temizlemek tek başına yetmez, React state'i de elle güncellemek gerekir).
- **Test e-postası:** `POST /api/admin/settings/smtp/test` — her zaman **kayıtlı** SMTP config'ini kullanır (önce Ayarlar'dan Kaydet, sonra Test).

---

## 🌐 REST API Endpoint Kataloğu

| Yöntem | Endpoint | Auth | Açıklama |
| :--- | :--- | :--- | :--- |
| `GET` | `/health` | — | Sunucu durum kontrolü |
| `POST` | `/api/auth/login` | — | Adım 1: e-posta/şifre → token veya `requiresMfa` + challengeToken |
| `POST` | `/api/auth/mfa/verify` | — | Adım 2: challengeToken + TOTP/kurtarma kodu → oturum token'ı |
| `POST` | `/api/auth/logout` | **Bearer** | Mevcut oturumu iptal eder |
| `GET` | `/api/auth/me` | **Bearer** | Giriş yapmış kullanıcının kimlik/rol/MFA durumu |
| `POST` | `/api/account/password` \| `/mfa/setup` \| `/mfa/enable` \| `/mfa/disable` | **Bearer** | Kendi şifre/MFA yönetimi (herkes) |
| `GET`/`POST` | `/api/admin/users*`, `GET /api/admin/audit-log` | **Bearer (Admin)** | Kullanıcı yönetimi ve denetim logu |
| `GET`/`POST`/`PUT`/`DELETE` | `/api/admin/security-profiles*` | **Bearer (Admin)** | Güvenlik profili CRUD |
| `POST` | `/api/devices/{id}/security-profile` | **Bearer (Admin)** | Cihaza güvenlik profili atar/kaldırır |
| `GET` | `/api/agents/{id}/security-profile` | AgentToken | Ajanın branding + şifre-gerektirir bayrakları (hash asla dönmez) |
| `POST` | `/api/agents/{id}/security/verify` | AgentToken | Panel/Çıkış/Kaldırma tek şifresini sunucuda doğrular |
| `GET`/`POST`/`PUT`/`DELETE` | `/api/admin/device-groups*` | **Bearer (Admin)** | Cihaz grupları (şirket/departman) CRUD |
| `POST` | `/api/devices/{id}/group` | **Bearer (Admin)** | Cihazı bir gruba atar/kaldırır |
| `GET` | `/api/alerts/active` | **Bearer (AnyUser)** | Şu an açık (çözülmemiş) tüm cihaz uyarılarını listeler |
| `POST` | `/api/admin/users/invite` | **Bearer (Admin)** | E-posta ile davet gönderir (geçici şifre yerine) |
| `POST` | `/api/admin/settings/smtp/test` | **Bearer (Admin)** | Kayıtlı SMTP config'iyle test e-postası gönderir |
| `GET` | `/api/invite/{token}` | — | Davet önizlemesi (davet kabul ekranı için) |
| `POST` | `/api/invite/{token}/accept` | — | Daveti kabul eder, şifre belirler, otomatik oturum açar |
| `POST` | `/api/agents/enroll` | EnrollmentKey | Yeni Windows Agent kayıt işlemi |
| `POST` | `/api/agents/{id}/heartbeat` | AgentToken | Agent periyodik telemetri (gerçek CPU/RAM/Disk/AgentVersion dahil) |
| `GET` | `/api/downloads` | — | MSI paket indirme kataloğunu döner |
| `GET` | `/downloads/{fileName}` | — | MSI dosyasını indirir |
| `POST` | `/api/remote-sessions` | **Bearer (AnyUser)** | Teknisyen için canlı `nexmote://` deep-link oturumu açar |
| `GET`/`POST` | `/api/settings` | **Bearer (Admin)** | Sunucu genel konfigürasyonunu okur/günceller |
| `GET` | `/api/devices` | **Bearer (AnyUser)** | Kayıtlı cihazların özet ve gerçek donanım metriklerini döner |
| `GET` | `/api/devices/{id}` | **Bearer (AnyUser)** | Tekil cihaz detayı |
| `DELETE` | `/api/devices/{id}` | **Bearer (Admin)** | Cihazı kalıcı olarak siler |
| `GET` | `/api/updates/check` | — | `downloads/versions.json`'dan okunan gerçek Agent/Technician sürüm ve OTA kataloğu |
| `POST` | `/api/agents/{id}/update` | **Bearer (AnyUser)** | Seçili (online) cihaza uzaktan sessiz Agent güncelleme sinyali gönderir |
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
6. **Cihaz Detay Paneli:** Sağdaki detay çekmecesi `Genel Bakış` (kimlik: OS, IP, aktif kullanıcı, domain, lokasyon, ajan sürümü, cihaz ID) ve `Cihaz Özellikleri` (Donanım envanteri, CPU, RAM, Disk seri numaraları) olarak ayrı sekmelere bölündü. Cihaz çevrimdışıysa hem Genel Bakış'ta son görülme zamanı gösterilir hem de Cihaz Özellikleri sekmesinde `.stale-data-notice` uyarı bandıyla verinin güncel olmayabileceği belirtilir.

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

### Sade Kurulum Akışı (Lisans Ekranı Atlanır, 2026-08-24)
Üç MSI de (`Agent`, `Technician`, `Cleaner`) zaten `WixUI_Minimal` kullanıyordu (kurulum dizini/özellik seçim ekranları hiç yok) ama hâlâ **Welcome → Lisans Sözleşmesi (oku/kabul et + İleri) → İlerleme → Bitiş** olmak üzere 4 ekran ve 3 tıklama gerektiriyordu. `scripts/build-msi.ps1`'deki her üç `Generate-*Wxs` fonksiyonunun `<UI>` bloğuna şu satır eklendi:
```xml
<Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="ProgressDlg" Order="2" Condition="1" />
```
Bu, kütüphanenin varsayılan `WelcomeDlg`→`LicenseAgreementDlg` publish'ini (`Order="1"`) ezer — Welcome ekranındaki tek "Kur" tıklaması artık doğrudan kuruluma geçiyor, Lisans ekranı hiç görünmüyor. **WiX v5 şema tuzağı:** `<Publish>` elementinin koşulu WiX v3'teki gibi inner text (`>1</Publish>`) olarak yazılamaz — WiX v5 şeması bunu `WIX0400: illegal inner text` hatasıyla reddeder, koşul mutlaka `Condition="..."` **attribute**'u olarak verilmeli (`<Publish ... Condition="1" />`). Sonuç: 4 ekran/3 tıklamadan **3 ekran/1 tıklamaya** indi (Welcome → Kur, İlerleme, Bitiş → Son + otomatik uygulama başlatma).

### MSI Yapısının Sadeleştirilmesi (2026-08-24)
Üç MSI'ın (Agent/Technician/Cleaner) paketleme betikleri denetlenip ölü/sapmış kod temizlendi:
- **Kaldırıldı:** Inno Setup `.exe` yolu (`agent-setup.iss`, `technician-setup.iss`, `package-windows.ps1`'deki ISCC derleme bloğu) — `DownloadCatalog` zaten hiçbir zaman `.exe` sunmuyordu (sadece `*.msi` arıyor), bu yol sadece build süresini uzatan ölü koddu. Ayrıca `scripts/installer-assets/` altındaki kullanılmayan eski WiX şablonları (`NexMote.Agent.wxs`/`NexMote.Technician.wxs` — `build-msi.ps1` kendi WXS'ini inline üretiyor, bunlara hiç dokunmuyordu), yarım kalmış TR/EN `.wxl` lokalizasyon dosyaları (hiçbir yerden `-culture` ile bağlanmıyordu) ve MSI'nin yaptığı işi elle tekrarlayan, davranışı MSI'dan **sapmış** (`sc.exe` ile servis DisplayName'i farklı, auto-restart config yok) `install-agent.ps1`/`uninstall-agent.ps1`/`install.bat`/`install-technician.ps1`/`uninstall-technician.ps1` script'leri silindi — bunlar hiçbir yerden çağrılmıyordu ama her MSI'ye sessizce paket dosyası olarak gömülüp Program Files'a kuruluyordu.
- **Zorunlu parametreler:** `package-windows.ps1`'in `-Version`/`-AgentReleaseNotes`/`-TechnicianReleaseNotes` parametreleri ve `build-msi.ps1`'in `-Version`'ı artık `Mandatory` — eskiden hardcoded bayat varsayılanları vardı (`"0.6.2"`), script parametresiz/eksik çağrılırsa artık sessizce eski sürüm üretmek yerine hata verip durur (v0.6.5'teki sürüm-sürüklenmesi bug'ıyla aynı sınıftan bir hatanın tekrarını önlemek için — bkz. yukarıdaki "Versiyonlama & Otomatik Güncelleme Mimarisi").
- **DownloadCatalog etiket düzeltmesi:** üç paket de yanlışlıkla "Çok Dilli (Multi-Language)" diye etiketlenmişti (MSI UI'ı gerçekte hep tek dilde/Türkçe deriliyor) — `"Türkçe"` olarak düzeltildi.
- **Cleaner MSI'de eksik ARP metadata tamamlandı:** Agent/Technician'da olan `ARPHELPLINK`/`ARPURLINFOABOUT`/`ARPURLUPDATEINFO`/`ARPCONTACT` Cleaner'a da eklendi (Add/Remove Programs tutarlılığı).
- **Bilinen/kabul edilmiş sınırlama:** hiçbir MSI code-signed değil (EV sertifika maliyeti nedeniyle şimdilik ertelendi) — bu turun kapsamı dışında bırakıldı.

---

## 🔒 Kurumsal Ajan Güvenlik Profilleri (Branding + Kısıtlı Tray Menüsü + Tek Şifre Koruması)

Web konsolundan yönetilen, cihazlara atanabilen **Güvenlik Profilleri** (`SecurityProfiles` tablosu, `SecurityProfileService`) ile ajanın davranışı kurumsal ihtiyaca göre kilitlenebilir:

- **Branding:** Profildeki `AgentDisplayName`/`IconBase64` doluysa Tray'in `NotifyIcon` metni/ikonu bunu kullanır (boşsa varsayılan "NexMote Agent" + kalkan ikonu).
- **Kısıtlı tray menüsü:** `RestrictTrayMenu=true` ise sağ tık menüsü sadece **"🛡️ Durum Panelini Görüntüle"** ve **"Çıkış"** içerir — Sunucu Ayarları, Güncelleme Kontrolü, Durumu Yenile kaldırılır.
- **Tek şifre koruması (2026-08-23'te sadeleştirildi):** Eskiden Durum Paneli/Çıkış/Kaldırma için 3 ayrı şifre vardı — kullanıcı isteğiyle **tek bir `RequirePassword`/`PasswordHash` alanına** indirildi: profilde şifre koruması açıksa aynı şifre her üç işlemi de (panel görme, ajanı kapatma, kaldırma) korur. `SecurityVerifyRequest.Action` alanı sadece denetim logunda hangi işlemin denendiğini ayırt etmek için kalır (`security.dashboard_verify`/`exit_verify`/`uninstall_verify`), doğrulamayı etkilemez. **Doğrulama her zaman sunucu üzerinden yapılır** — ajan hiçbir zaman şifre veya hash saklamaz (`POST /api/agents/{id}/security/verify`, `AgentToken` ile korunur). Bağlantı yoksa korumalı işlem **fail-closed** durur (bağlantıyı kesip korumayı atlatamazsınız).
- **Dağıtım mekanizması:** Tray, periyodik olarak `GET /api/agents/{id}/security-profile?agentToken=...`'ı sorgular (branding + tek `requirePassword` bool döner, **şifre hash'i asla dönmez**) ve menü/ikon'u buna göre yeniden kurar (`TrayApplicationContext.RefreshSecurityProfileAsync`/`BuildContextMenu`, `src/NexMote.Agent.Tray/Program.cs`).
- **Kaldırma koruması sadece `NexMote.Cleaner`'ı korur** (`Main`, elevation kontrolünden hemen sonra) — Windows'un kendi "Program Kaldır"/`msiexec /x` akışı kapsam dışı, zaten yerel yönetici yetkisi gerektiriyor. Silent modda `--password=<şifre>` argümanıyla script'li/yetkili kaldırma desteklenir.
- **Bir profil atanmamış (ve grubu üzerinden de miras alınan bir profili olmayan) cihazda hiçbir kısıtlama yoktur** — geriye dönük tamamen uyumlu, mevcut davranış aynen korunur.
- **Bu özellik Agent MSI'ının yeniden paketlenip dağıtılmasını gerektirir** (Tray/Cleaner binary'leri değişti) — sadece API/Web deploy'u mevcut cihazlardaki ajanlara yansımaz, `scripts\package-windows.ps1` ile yeni bir MSI üretilip `/api/agents/{id}/update` veya elle dağıtılmalı.

### Cihaz Grupları (Şirket/Departman — İç İçe, 2026-08-23)

Cihazlar `DeviceGroups` tablosuyla **keyfi derinlikte iç içe gruplar** halinde organize edilebilir (`DeviceGroupEntity`: `Name`, `ParentGroupId` — kendine referans, `DefaultSecurityProfileId`). Örn: "Talay Lojistik" (şirket, üst grup yok) → "Muhasebe" (departman, `ParentGroupId` = şirket). `DeviceEntity.GroupId` ile bir cihaz bir gruba atanır (`DeviceGroupService`, `POST /api/devices/{id}/group`).

- **Kademeli (inherited) güvenlik profili çözümlemesi:** `SecurityProfileService.ResolveEffectiveProfile(deviceId)` şu sırayla arar — (1) cihazın kendi `SecurityProfileId`'si varsa **her zaman kazanır** (grup mirasını ezer), (2) yoksa cihazın grubundan başlayıp `ParentGroupId` zincirinde yukarı doğru ilk `DefaultSecurityProfileId` dolu olan grup kullanılır (departmanda yoksa şirkette aranır — cycle-safe, `HashSet` ile ziyaret takibi), (3) hiçbiri yoksa kısıtlama yok. Bu tek helper hem `GetAgentProfile` hem `VerifyPassword` tarafından kullanılır.
- **Silme/döngü koruması:** Alt grubu olan bir grup silinemez (400 döner); bir grubun `ParentGroupId`'sini kendi alt zincirindeki bir gruba çekmek döngü oluşturacağından reddedilir (`DeviceGroupService.CreatesCycle`).
- **Web:** "Cihaz Grupları" ekranı (Admin-only, `Building2` ikonu) grup oluşturma/düzenleme formu (Ad, Üst Grup, Varsayılan Güvenlik Profili) + girintili ağaç tablosu sağlar; cihaz detay panelindeki "Oturum & Güvenlik" kartında "Güvenlik Profili" seçicinin yanında bir "Grup" seçici bulunur.
- **Genişletilebilirlik:** Şu an sadece grup + varsayılan güvenlik profili temeli var — kullanıcı ileride departman/şirket bazlı başka özellikler (farklı ayarlar, branding vb.) eklemeyi planlıyor; `DeviceGroupEntity`/`DeviceGroupService` bu amaçla genişletilebilir tutuldu.

---

## 🚨 Uyarı / Bildirim Sistemi (Cihaz Offline, Disk/CPU/RAM Eşik Aşımı, 2026-08-24)

Üç kişilik (IT uzmanı/yazılım uzmanı/satış) perspektifinden yapılan rekabet analizinde en kritik eksik olarak belirlenen **proaktif uyarı sistemi** eklendi — artık bir cihaz çevrimdışı kaldığında veya disk/CPU/RAM eşiği aşıldığında admin(ler)e otomatik e-posta gider, panele bakmaya gerek kalmaz.

- **4 kural, ayrı ayrı açılıp eşiği ayarlanabilir** (`ServerSettings` tablosunda `Alert*` alanları, Ayarlar ekranında "Uyarılar" kartı): **Offline** (varsayılan: 5dk heartbeat yok, açık), **DiskLow** (varsayılan: boş disk < 5000 MB, açık), **CpuHigh** (10dk ortalama CPU > eşik, **varsayılan kapalı** — gürültülü olabilir), **MemoryHigh** (kullanılan RAM % > eşik, **varsayılan kapalı**).
- **Alıcılar:** `ServerSettings.AlertRecipientEmails` (virgülle ayrık). Boşsa tüm aktif **Admin** kullanıcılarının e-postalarına gönderilir (fallback, ek konfigürasyon gerektirmeden çalışır).
- **Durum takibi ve gürültü kontrolü:** `DeviceAlerts` tablosu her `(DeviceId, AlertType)` çifti için en fazla bir **açık** (`ResolvedAt == null`) kayıt tutar. İlk tetiklenmede e-posta gider; açık kaldığı sürece **4 saatte bir** hatırlatma gider (`LastNotifiedAt` kontrolü); koşul düzeldiğinde `ResolvedAt` set edilip tek seferlik "✅ düzeldi" e-postası gider. Bu mantığın tamamı `AlertService.EvaluateAndNotifyAsync` içinde, `SecurityProfileService`/`DeviceGroupService` ile aynı `IDbContextFactory<AppDbContext>` singleton deseniyle.
- **Değerlendirme motoru:** `AlertMonitorService` (`BackgroundService`, API sürecinde **ilk kez** eklenen periyodik hosted service) — sunucu her açıldığında bir kez hemen, sonra **2 dakikada bir** `AlertService.EvaluateAndNotifyAsync()` çalıştırır. Disk/CPU/RAM kuralları anlık cihaz değerine bakar (`MemoryTotalMb > 0` guard'ı — henüz hiç telemetri göndermemiş yeni kayıtlı cihazlarda yanlış pozitif üretmesin diye); Offline kuralı `now - LastSeenAt` farkına bakar.
- **Endpoint:** `GET /api/alerts/active` — şu an açık tüm uyarıları (`{deviceId, alertType, triggeredAt}`) döner. Bilinçli olarak `authed` (AnyUser) grubunda — Teknisyenler de görsün diye — ama eşik/alıcı **ayarlarını değiştirmek** hâlâ `/api/settings` üzerinden Admin-only.
- **Web entegrasyonu:** Cihaz listesindeki "Dikkat" filtresi (`isWarning`/`warningCount`, eskiden sadece "ajan sürümü eski" kontrolü yapıyordu) artık aktif uyarısı olan cihazları da kapsar. Cihaz detay panelinin "Genel Bakış" sekmesinde, o cihaza ait aktif uyarı varsa `.stale-data-notice` (mevcut amber uyarı bandı deseni) ile insan-okunur bir özet gösterilir.
- **E-posta gönderimi mevcut `EmailService`/SMTP config'ini yeniden kullanır** — ayrı bir alt sistem değil, davet/test e-postalarıyla aynı `SendAsync(toEmail, subject, htmlBody)` çağrısı.

---

## 🖥️ Teknisyen Masaüstü Uygulaması (WPF) Kuralları

1. **Gerçek Login Ekranı (2026-08-23'te değişti):** Çoklu kullanıcı + MFA mimarisiyle birlikte "sabit admin/admin123 ile sessiz giriş" kuralı kaldırıldı — artık her teknisyen **kendi hesabıyla** giriş yapar. Uygulama açılışta yerelde DPAPI (`DataProtectionScope.CurrentUser`) ile şifreli saklanan bir oturum token'ı varsa `/api/auth/me` ile geçerliliğini sessizce doğrular ve login sormadan devam eder; token yok/süresi dolmuşsa `ServerLoginWindow` gösterilir (e-posta/şifre, ardından hesapta MFA açıksa 6 haneli kod adımı). **Parola hiçbir zaman diske yazılmaz** — sadece üretilen opak oturum token'ı (`MainWindow.TechnicianAppSettings`, `%AppData%\NexMote\TechnicianApp\settings.json`) saklanır.
2. **Local IP Engelleme:** Sunucu bağlantı adreslerinde `192.168...`, `127.0.0.1` veya `http://` tespiti halinde otomatik `https://nexmote.com` adresine zorlama yapılır.
3. **Çoklu Ekran Yönetimi (Soldan Sağa Sıralı & Esnek Seçim):** Monitörler `Bounds.Left` değerine göre **kesin olarak soldan sağa** sıralanır. Teknisyen üst bardaki monitör seçiciden `🖥️ Tüm Ekranlar` (yan yana eş zamanlı akış) modunu veya tek tek `🖥️ Ekran 1`, `🖥️ Ekran 2` seçerek o ekranı tam boyutta izlemeyi seçebilir.
4. **Otomatik Görüntü Kalitesi:** Manuel kalite butonu ve rozetler kaldırıldı. Agent, her karenin SignalR gönderim süresine bakarak JPEG kalitesini (20-80 aralığında) kendiliğinden ayarlar.
5. **Görünüm Modları (Sığdır / Yay / 1:1):** `📐 Ekrana Sığdır` (orantılı), `↔️ Ekrana Yay` (tam doldur) ve `🔍 Orijinal (1:1)` modları mevcuttur; fare/klavye haritalaması her mod için normalize edilmiştir.
6. **Yönetici Olarak Çalıştır (UAC):** Komut panelinde "🛡️ Yönetici Olarak Çalıştır" kutucuğu işaretlenirse, agent komutu Windows `runas` ile başlatır — hedef cihazda gerçek UAC istemi çıkar.
7. **Görüntü Dayanıklılığı:** Donma/kopma durumunda 6 saniyelik Watchdog otomatik yenileme tetikler; `🔄 Ekranı Yenile` ve `F11 Tam Ekran` mevcuttur.
8. **Sadeleştirilmiş Toolbar & Temiz Canlı Ekran:** Pano Gönder, Win+D, Alt+Tab, Ctrl+Alt+Del, Cihaz Listesi ve Otomatik Kalite rozeti uzaktan bağlantı ekranından kaldırıldı. `🚀 Güncelleme Kontrol Et` butonu sadece ana Cihaz Listesi ekranında görünür, canlı oturumda teknisyenin dikkatini dağıtmaz.

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
4. **Oturum Yönetimi (Login), Roller ve MFA:**
   - Web konsolu ve Teknisyen uygulaması artık **çoklu kullanıcı** (Admin + Teknisyen rolleri) destekler, her kullanıcı kendi e-posta/şifresiyle giriş yapar (bkz. Çoklu Kullanıcı Kimlik Doğrulama, Roller ve MFA). Oturum token'ı web'de `localStorage`/`sessionStorage`'da, Teknisyen uygulamasında DPAPI şifreli olarak diskte saklanır.
   - **Rol bazlı UI:** Web konsolunda "Kullanıcı Yönetimi" ve "Denetim Logu" sekmeleri ile Sunucu Ayarları kartı sadece `Admin` rolüne görünür; Teknisyen rolü cihaz listesi/uzak oturum/komut çalıştırmayı görebilir ama yönetimsel ekranlara erişemez. Herkes kendi "Hesap Ayarları"ndan şifresini değiştirebilir ve MFA'yı (TOTP, isteğe bağlı) açıp kapatabilir.

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
.\.dotnet\dotnet.exe run --project src/NexMote.Api/NexMote.Api.csproj --urls "http://127.0.0.1:5080"
```
Yerel geliştirmede bootstrap Admin: `admin@nexmote.com` / `admin123` (`Admin:Email`/`Admin:Password`, appsettings.json'da tanımlı, sadece `Users` tablosu boşken ilk açılışta kullanılır), `Enrollment:Key` = `dev-enrollment-key` (production'da KULLANILMAZ).

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
.\.dotnet\dotnet.exe publish src/NexMote.Api/NexMote.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish-linux

# 2. Web ön yüzünü derle ve kopyala
cmd /c "npm --prefix web run build"
powershell -Command "New-Item -ItemType Directory -Force -Path 'publish-linux\wwwroot'; Copy-Item -Recurse -Force 'web\dist\*' 'publish-linux\wwwroot\'"

# 3. Ziple ve SCP ile sunucuya yükle
powershell -Command "Compress-Archive -Path 'publish-linux\*' -DestinationPath 'publish-linux.zip' -Force; scp -i '$env:USERPROFILE\.ssh\id_ed25519' 'publish-linux.zip' root@186.241.21.133:/tmp/publish-linux.zip"

# 4. Sunucuda aç ve servisi yeniden başlat
powershell -Command "ssh -i '$env:USERPROFILE\.ssh\id_ed25519' root@186.241.21.133 'unzip -o /tmp/publish-linux.zip -d /var/www/nexmote/ && systemctl restart nexmote.service'"

# 5. MSI'ları HER İKİ downloads klasörüne de yükle (bkz. yukarıdaki not)
scp -i "$env:USERPROFILE\.ssh\id_ed25519" downloads\NexMote-Agent-Setup.msi downloads\NexMote-Technician-Setup.msi downloads\versions.json root@186.241.21.133:/var/www/nexmote/wwwroot/downloads/
scp -i "$env:USERPROFILE\.ssh\id_ed25519" downloads\NexMote-Agent-Setup.msi downloads\NexMote-Technician-Setup.msi downloads\versions.json root@186.241.21.133:/var/www/nexmote/downloads/
```

---

## 🌐 Canlı Sunucu (Production) Özeti

- **İşletim Sistemi:** Ubuntu 24.04.4 LTS (Hostinger Germany - Frankfurt VPS - `186.241.21.133`)
- **Web Sunucu:** Nginx (`/etc/nginx/sites-available/nexmote`) - Reverse Proxy & WebSocket Headers
- **SSL Sertifikası:** Let's Encrypt Certbot 256-bit SSL (`https://nexmote.com`, `https://www.nexmote.com`, `https://api.nexmote.com`)
- **Servis Yöneticisi:** `systemd` (`nexmote.service` -> `/var/www/nexmote/NexMote.Api.dll --urls http://127.0.0.1:5080`)
  - Sırlar: `/etc/systemd/system/nexmote.service.d/override.conf` (`Admin__ApiKey`, `Enrollment__Key` — repoya işlenmez)
- **Veritabanı:** SQLite (`/var/www/nexmote/nexmote.db`)
- **MFA Data Protection anahtarları:** `/var/www/nexmote/dpkeys/` — kullanıcıların TOTP secret'larını şifreleyen anahtarlar burada kalıcı. `unzip -o` ile yapılan deploy bu dizine dokunmaz (zip içinde yer almaz), ama **elle silinirse tüm kullanıcıların MFA'sı kalıcı olarak çözülemez hale gelir** (şifre girişi etkilenmez, sadece MFA'yı herkesin yeniden kurması gerekir).
- **Kayıtlı test/demo cihazları:** TAL-01888 (aktif test cihazı), DESKTOP-SIH3FAC (kullanıcının kendi bilgisayarı — 2026-08-22'de doğrulandı, test için kullanılabilir), 36D6735F-A0A6-4, PC-UFUK

---

## 🔍 Log Konumları, Kurulum Parametreleri & Sorun Giderme

### 1. Kurulum ve Çalışma Logları (Agent & Teknisyen)
- **Windows Servisi ve Ajan Logları:** `C:\ProgramData\NexMote\Logs\agent-service.log`
- **Süreç Başlatma Hata Logları:** `C:\ProgramData\NexMote\Logs\agent-service-startup-error.log`
- **Genel Sistem Logları:** `C:\ProgramData\NexMote\Logs\`

### 2. Kurulum ve Sessiz Dağıtım Parametreleri
- **Windows Installer (`.msi`):** tek dağıtım formatı — Agent, Technician ve Cleaner üçü de WiX ile üretilir (Inno Setup `.exe` yolu 2026-08-24'te kaldırıldı: `DownloadCatalog` zaten hiçbir zaman `.exe` sunmuyordu, sadece build süresini uzatan ölü kod duruyordu).
  - Active Directory GPO / Intune sessiz kurulum:
    ```cmd
    msiexec /i NexMote-Agent-Setup.msi /qn /norestart
    ```

### 3. Sık Karşılaşılan Durumlar ve Çözümleri
- **Cihaz Web Konsolunda "Çevrimiçi" Görünmüyor:**
  - `C:\ProgramData\NexMote\Logs\agent-service.log` dosyasını inceleyin. `ServerUrl`'in `https://nexmote.com` olduğundan ve ağ güvenlik duvarının outbound 443 portunu engellemediğinden emin olun.
- **Kullanıcı Oturumunda Tepsi Simgesi Çıkmıyor:**
  - `Task Manager` üzerinden `NexMote.Agent.Tray.exe` sürecinin çalışıp çalışmadığını kontrol edin. Servis `RunSessionWatchdogAsync` ile her saniye oturumu denetler.

---

*Gelecekte projeye müdahale edecek tüm AI geliştiriciler bu master rehberdeki mimariye, veritabanı şemasına ve tasarım kurallarına bağlı kalmalıdır.*

