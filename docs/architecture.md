# NexMote - Detaylı Sistem Mimarisi ve Modül Rehberi

Bu doküman, **NexMote** uzaktan bilgisayar yönetimi ve destek platformunun mimari katmanlarını, tüm bileşenlerini, dosya görev dağılımını, veri akışlarını ve güvenlik protokollerini detaylandırmaktadır.

---

## 🏗️ 1. Genel Mimari Şeması

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
│  │ ├── Pending MSI Otomatik Kurulumu       │  │ │  ├── Çift Yönlü Dosya Aktarımı       │
│  │ └── 1s Session Watchdog & Launcher      │  │ │  └── Güç Yönetimi & Ctrl+Alt+Del     │
│  └────────────────────┬────────────────────┘  │ └──────────────────────────────────────┘
│                       │ (CreateProcessAsUser  │
│                       │  + SeTcbPrivilege)    │
│  ┌────────────────────▼────────────────────┐  │
│  │ Aktif Oturum Süreçleri (User Session)   │  │
│  │ ├── NexMote.Agent.Tray.exe (--tray)     │  │
│  │ │   ├── Tray Icon & Dashboard Paneli    │  │
│  │ │   ├── Çoklu Ekran GDI+ Yakalama       │  │
│  │ │   └── Dinamik JPEG Kalite Ayarı       │  │
│  │ └── NexMote.Agent.Tray.exe              │  │
│  │     (--input-helper / SYSTEM Yetkili)   │  │
│  │     └── Named Pipe Girdi Enjektörü      │  │
│  │         (UAC Onay Tıklamaları İçin)     │  │
│  └─────────────────────────────────────────┘  │
└───────────────────────────────────────────────┘
```

---

## 📁 2. Klasör Yapısı ve Modül Haritası

```
NexMote/
├── NexMote.sln               # Ana Visual Studio Solution Dosyası (Tüm src/ projelerini içerir)
├── AGENTS.md                 # Master AI Geliştirici ve Mimari Kılavuzu
│
├── src/                      # Tüm .NET 8 Kaynak Kodları (Tek Çatı Altında)
│   ├── NexMote.Api/          # ASP.NET Core 8 Web API & SignalR Sunucusu
│   │   ├── Auth/             # AdminAuthFilter.cs (Bearer token korumalı endpoint filtresi)
│   │   ├── Data/             # AppDbContext (Entity Framework Core SQLite)
│   │   ├── Hubs/             # SignalingHub.cs (/hubs/signaling - WebSocket Canlı Akış)
│   │   ├── Services/         # DeviceRegistry, RemoteSessionRegistry, DownloadCatalog
│   │   ├── wwwroot/          # Üretilen React Web Ön Yüzü Statik Dosyaları (Vite dist)
│   │   └── Program.cs        # Minimal API başlangıcı, CORS, SignalR rota haritası
│   │
│   ├── NexMote.Agent.Windows/# Windows Background Service (LocalSystem)
│   │   ├── Worker.cs         # 20s Heartbeat, pending MSI kurulumu, 1s Session Watchdog
│   │   ├── SessionProcessLauncher.cs # Win32 TCB yetkisi, token duplikasyonu & SYSTEM oturum enjeksiyonu
│   │   ├── CpuUsageSampler.cs# GetSystemTimes ile 10 dakikalık kayan pencere gerçek CPU telemetrisi
│   │   ├── SystemTelemetry.cs# GlobalMemoryStatusEx RAM, DriveInfo Disk, fiziksel IPv4 tespiti
│   │   ├── DeviceIdentityStore.cs # %ProgramData%\NexMote\Agent\identity.json kimlik yöneticisi
│   │   └── AgentClient.cs    # Sunucu REST API istemcisi
│   │
│   ├── NexMote.Agent.Tray/   # Kullanıcı Oturumu Ekran Yayını & Tray Uygulaması
│   │   └── Program.cs        # DashboardForm, RemoteScreenStreamer, InputHelperServer (Named Pipe),
│   │                         # ScreenCapture, InputInjector, DesktopHelper, SasHelper, CommandRunner
│   │
│   ├── NexMote.TechnicianApp/# Teknisyen Masaüstü Uygulaması (WPF .NET 8)
│   │   ├── MainWindow.xaml   # Canlı çoklu ekran yayını, nexmote:// deep-link, terminal paneli
│   │   └── ServerLoginWindow.xaml # Admin kimlik doğrulama diyaloğu
│   │
│   └── NexMote.Shared/       # Ortak Veri Tipleri & Kontrat Kütüphanesi
│       └── Contracts/        # 17 Adet DTO ve Protokol Record'u
│
├── web/                      # React 18 + TypeScript + Vite Web Teknisyen Konsolu
│   ├── src/
│   │   ├── App.tsx           # Ana UI (Login, Cihaz Listesi, Donanım Metrikleri, Terminal, İndirmeler)
│   │   ├── api.ts            # REST API Fetch Kontratları ve admin token yönetimi
│   │   └── styles.css        # Vanilla CSS SaaS Tasarım Sistemi
│   └── package.json
│
├── scripts/                  # Derleme, Paketleme ve Kurulum Betikleri
│   ├── package-windows.ps1   # Agent + Technician'ı publish edip MSI'ları üreten ana betik
│   ├── build-msi.ps1         # WiX v4 ile dinamik MSI derleyici
│   └── installer-assets/     # MSI localization (wxl) ve fallback yükleme scriptleri
│
├── docs/                     # Sistem Dokümantasyonları ve Güvenlik
│   ├── architecture.md       # Sistem Mimarisi ve Veri Akış Şeması
│   ├── security-model.md     # Güvenlik ve Yetkilendirme Modeli
│   ├── server-credentials.md # VPS IP, Root Şifresi ve SSH Anahtarları
│   └── infra/                # dev-run.md, iis-publish.md
│
├── assets/                   # Uygulama İkonları ve Grafikler (nexmote.ico)
└── downloads/                # Üretilen Kurulum Paketleri (MSI/ZIP) ve versions.json
```

---

## 🔐 3. Güvenlik, Kimlik Doğrulama & UAC Mimarisinin Detayları

### 1. Admin Kimlik Doğrulama (Bearer Token)
- Web konsolu veya Teknisyen uygulaması `/api/auth/login` endpoint'ine `{email, password}` gönderir.
- Doğrulama başarılı olduğunda sunucu `Admin:ApiKey` değerini döner.
- Bu token `Authorization: Bearer <token>` olarak `/api/devices`, `/api/settings`, `/api/remote-sessions` gibi korumalı endpoint'lerde zorunlu kılınır (`AdminAuthFilter`).

### 2. Cihaz Güvenliği & Heartbeat Doğrulaması
- Cihaz sunucuya ilk kaydolurken `EnrollmentKey` doğrulanır.
- Kayıt başarılı olunca cihaza özel 32-byte rastgele `AgentToken` üretilir ve `%ProgramData%\NexMote\Agent\identity.json` dosyasına yazılır.
- Sonraki tüm heartbeat ve komut audit isteklerinde bu `AgentToken` doğrulanır.

### 3. UAC Görünürlüğü & UIPI Atlatma (SYSTEM Named Pipe)
Standart uzaktan erişim araçlarının karşılaştığı iki büyük Windows güvenlik engeli NexMote mimarisinde şu şekilde çözülmüştür:
1. **Secure Desktop İzolasyonu**: UAC normalde izole bir masaüstünde açılır. Windows Servisi başlangıçta `HKLM\...\Policies\System\PromptOnSecureDesktop = 0` değerini ayarlayarak UAC isteminin normal masaüstünde açılmasını ve ekran yakalama tarafından görülebilmesini sağlar.
2. **UIPI (User Interface Privilege Isolation)**: Kullanıcı yetkisindeki süreçlerin UAC pencerelerine tıklaması Windows tarafından engellenir. Windows Servisi (`SessionProcessLauncher`), SYSTEM belirtecini aktif oturuma bağlayarak `NexMote.Agent.Tray.exe --input-helper` sürecini **SYSTEM yetkisinde kullanıcının masaüstünde** başlatır. Tray uygulaması fare tıklamalarını yerel bir Named Pipe üzerinden bu SYSTEM yardımcısına iletir; SYSTEM yetkili yardımcı tıklamayı enjekte ederek UAC onay pencerelerinin uzaktan sorunsuz tıklanmasını sağlar.

---

## 🔄 4. Veri Akışları (Data Flows)

### A. Cihaz Kayıt (Enrollment) ve Heartbeat Akışı
```
Agent Windows Service                 NexMote Backend Sunucusu
        │                                        │
        ├── POST /api/agents/enroll (Key) ──────>│ (Kayıt Kontrolü)
        │<── 200 OK (DeviceId + AgentToken) ─────┤ (identity.json'a kaydet)
        │                                        │
[ Her 20sn Döngü ]                              │
        ├── POST /api/agents/{id}/heartbeat ────>│ (CPU, RAM, Disk, Uptime)
        │<── 204 NoContent ──────────────────────┤ (Devices tablosu güncellenir)
```

### B. Canlı Uzaktan Bağlantı Akışı
```
Web Konsolu            Backend API & SignalR Hub           Teknisyen Uygulaması           Hedef Agent (Tray)
     │                             │                                │                             │
     ├── POST /api/remote-sessions │                                │                             │
     │<── 200 OK (nexmote://...) ──┤                                │                             │
     │                             │                                │                             │
     └── (nexmote:// tetiklenir) ──────────────────────────────────>│                             │
                                   │<── JoinTechnicianSession ──────┤                             │
                                   ├── RemoteSessionRequested ───────────────────────────────────>│
                                   │<── JoinDeviceSession ────────────────────────────────────────┤
                                   ├── DeviceJoinedSession ────────>│                             │
                                   │                                │                             │
                                   │<══ screen-frame-multi (JPEG) ════════════════════════════════╡
                                   ├═══ screen-frame-multi ════════>│                             │
                                   │                                │                             │
                                   │<══ remote-input (Fare/Tuş) ════╡                             │
                                   ├═══ remote-input ════════════════════════════════════════════>│ (Named Pipe ->
                                   │                                │                             │  InputHelper -> UAC)
```

---

## 📊 5. Veritabanı Şeması (SQLite)

Veritabanı dosyası sunucuda `nexmote.db` yolunda tutulur.

### `Devices` Tablosu
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | Guid (PK) | Cihaz benzersiz kimliği |
| `DeviceName` | string | Bilgisayar adı |
| `DomainName` | string | Çalışma grubu / Domain |
| `OperatingSystem` | string | İşletim sistemi sürümü |
| `AgentVersion` | string | Çalışan gerçek Agent sürümü |
| `AgentToken` | string | Cihaza özel güvenlik sinyal token'ı |
| `ActiveUser` | string | Oturum açmış kullanıcı adı |
| `IpAddress` | string | Yerel fiziksel IPv4 adresi |
| `CpuUsagePercent` | double | 10 dakikalık kayan pencere ortalama CPU kullanım % |
| `MemoryTotalMb` | long | Toplam fiziksel RAM (MB) |
| `MemoryUsedMb` | long | Kullanılan RAM (MB) |
| `DiskFreeMb` | long | Sistem sürücüsü boş alan (MB) |
| `UptimeSeconds` | long | Sistemin açık kalma süresi (sn) |
| `LastSeenAt` | DateTimeOffset | Son heartbeat / sinyal zamanı |
| `EnrolledAt` | DateTimeOffset | Cihazın ilk kayıt zamanı |

### `RemoteSessions` Tablosu
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | Guid (PK) | Oturum benzersiz kimliği |
| `DeviceId` | Guid | Hedef cihaz kimliği |
| `Token` | string | Oturum güvenlik token'ı |
| `CreatedAt` | DateTimeOffset | Oturum oluşturulma zamanı |
| `ExpiresAt` | DateTimeOffset | Oturum geçerlilik bitiş zamanı (5 dakika) |

### `ServerSettings` Tablosu
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | int (PK) | Ayar ID (1) |
| `ServerUrl` | string | Sunucu genel adresi (`https://nexmote.com`) |
| `EnrollmentKey` | string | Ortak cihaz kayıt anahtarı |
| `HeartbeatSeconds` | int | Heartbeat gönderim sıklığı (20sn) |
| `DefaultLocationCode` | string | Varsayılan lokasyon kodu |
| `UpdatedAt` | DateTimeOffset | Son güncelleme tarihi |

### `CommandAudits` Tablosu
| Kolon | Tip | Açıklama |
| :--- | :--- | :--- |
| `Id` | Guid (PK) | Denetim kaydı kimliği |
| `DeviceId` | Guid | Cihaz kimliği |
| `SessionId` | Guid | Oturum kimliği |
| `Shell` | string | Kabuk ("cmd" / "powershell") |
| `Command` | string | Çalıştırılan komut metni |
| `ExitCode` | int | İşlem çıkış kodu |
| `StdOutPreview` | string | Standart konsol çıktısı özeti |
| `StdErrPreview` | string | Hata çıktısı özeti |
| `DurationMs` | long | Çalışma süresi (ms) |
| `ExecutedAt` | DateTimeOffset | Yürütülme zaman damgası |

---

*Bu mimari doküman, NexMote projesinin en güncel ve eksiksiz teknik referansıdır.*
