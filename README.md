# 🚀 NexMote - Kurumsal Düzeyde Uzaktan Bilgisayar Yönetimi & Canlı Destek Platformu

<div align="center">

[![Live System](https://img.shields.io/badge/Canlı_Sistem-nexmote.com-2563EB?style=for-the-badge&logo=google-chrome&logoColor=white)](https://nexmote.com)
[![Status](https://img.shields.io/badge/Durum-Çevrimiçi_%26_Aktif-10B981?style=for-the-badge)](https://nexmote.com/health)
[![.NET 8](https://img.shields.io/badge/.NET-8.0_LTS-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![React 18](https://img.shields.io/badge/React-18_%2B_TypeScript-61DAFB?style=for-the-badge&logo=react&logoColor=black)](https://react.dev/)
[![WiX v4](https://img.shields.io/badge/Installer-WiX_v4_MSI_%2B_InnoSetup-FF6F00?style=for-the-badge)](https://wixtoolset.org/)

**Kurumsal ölçekte, yüksek performanslı, self-hosted, tam donanım telemetrisi, canlı ekran kontrolü, uzaktan sessiz yönetim ve PowerShell/CMD terminal platformu.**

[Özellikler](#-kapsamlı-özellik-listesi) • [Kullanılan Teknolojiler](#-kullanılan-yapı-ve-teknolojiler) • [Sistem Mimarisi](#-sistem-mimarisi-ve-veri-akışı) • [Ajan Ana Yasası](#-değiştirilemez-ajan-ana-yasası-4-temel-madde) • [Kurulum & Dağıtım](#-kurulum-paketleme-ve-dağıtım)

</div>

---

## 🌟 Proje Özeti & Vizyon

**NexMote**, kurumsal BT departmanları, sistem yöneticileri, teknik servisler ve son kullanıcı destek ekipleri için geliştirilmiş yeni nesil bir uzaktan yönetim çözümüdür. 

Windows istemcileri üzerinde `LocalSystem` yetkisiyle çalışan arka plan servisi, kullanıcı oturumuna otomatik enjekte olan hafif sistem tepsisi (Tray) ajanı, WPF tabanlı teknisyen konsolu ve zengin React Web Yönetim Paneli ile uçtan uca modern bir ekosistem sunar.

---

## 📋 Kapsamlı Özellik Listesi

### 🖥️ 1. Ultra Düşük Gecikmeli Canlı Masaüstü & Çoklu Ekran Yönetimi
- **Eş Zamanlı Çoklu Monitör Yayını:** Cihaza bağlı tüm fiziksel ekranlar bağımsız kare hızlarıyla eş zamanlı olarak yayınlanır.
- **4 Kademeli Adaptif Akış Motoru:** Ağ gecikmesini (RTT) anlık ölçerek dinamik JPEG kalitesi ve kare hızını ayarlar:
  - `⚡ Hız Modu (Speed):` Düşük bant genişliğinde ultra akıcı 30+ FPS.
  - `⚖️ Dengeli Mod (Balanced):` Standart kurumsal destek için optimize edilmiş kalite ve hız.
  - `💎 Kristal Kalite (Quality):` 90% JPEG kalitesiyle tasarım ve detaylı metin incelemesi.
  - `🤖 Akıllı Otomatik (Auto):` Ağ durumuna göre anlık otomatik profil geçişi.
- **Çift Yönlü Giriş (Fare & Klavye):** Tıklama, sürükleme, fare tekerleği, özel tuş kombinasyonları (`Ctrl+Alt+Del`, `Win+R`, `Alt+Tab` vb.).
- **Özel Protokol Bağlantısı (`nexmote://`):** Web panelinden "Canlı Masaüstü" butonuna tıklandığında masaüstü teknisyen uygulamasını otomatik tetikler.

### 🛡️ 2. UAC (Kullanıcı Hesabı Denetimi) & Secure Desktop Aşımı
- **SYSTEM Seviyesinde Girdi Enjeksiyonu (`--input-helper`):** Windows Güvenli Masaüstü (Secure Desktop) ve UIPI kısıtlamalarını aşarak UAC onay pencerelerine teknisyenin uzaktan sorunsuz tıklayabilmesini sağlar.
- **Sessiz Arka Plan Yetkilendirmesi:** Web üzerinden gönderilen komutlar ve uygulama kaldırma işlemleri masaüstünde kullanıcıya herhangi bir UAC onay penceresi fırlatmadan doğrudan `SYSTEM` yetkisiyle sessizce yürütülür.

### 📊 3. Derin Donanım & Bileşen Seri Numarası Envanteri
- **Cihaz & Kasa Seri Numarası:** Dell Service Tag, Lenovo/HP Serial vb. donanımsal seri numarası (tek tıkla panoya kopyalama).
- **Anakart (BaseBoard) Seri No:** Üretici, model adı ve fabrikasyon anakart seri numarası.
- **BIOS / UEFI Seri No:** Sürüm, yayın tarihi ve BIOS seri numarası.
- **İşlemci (CPU) Envanteri & Processor ID:** Model, mimari, çekirdek/iş parçacığı sayısı, maksimum saat hızı ve **CPU ID**.
- **Fiziksel RAM Modülleri (Slot Bazlı):** Her slotun konumu, bellek kapasitesi (GB), hızı (MHz), üreticisi, parça kodu (Part Number) ve **RAM Modül Seri Numarası**.
- **Depolama Sürücüleri (SSD / NVMe / HDD):** Model adı, arayüz (NVMe/SATA), medya türü, disk boyutu, bölüm sayısı ve **Disk Seri Numarası**.
- **Ekran Kartları (GPU):** Grafik kartı modelleri, sürücü sürümleri ve video belleği (VRAM).
- **Ağ Bağdaştırıcıları (NIC):** Fiziksel/sanal ağ kartları, MAC adresleri, yerel IPv4/IPv6, alt ağ maskesi, ağ geçidi ve DNS sunucuları.

### 💻 4. Kullanıcı Dostu Windows Sürüm Tespiti
- Ham NT çekirdek dizgisi (`Microsoft Windows NT 10.0.26200.0`) yerine gerçek ticari sürüm tespiti:
  - `Windows 11 Pro (24H2) [26200]`
  - `Windows 11 Enterprise (23H2) [22631]`
  - `Windows 10 Pro (22H2) [19045]`
  - `Windows Server 2025 / 2022 / 2019 Datacenter`

### ⚡ 5. Canlı Uzak Terminal (Web Shell)
- **Web Üzerinden Canlı Komut:** Web konsolundaki terminal sekmesinden CMD veya PowerShell komutları çalıştırma.
- **Canlı Log Akışı & Denetim:** Standart çıktı (`stdout`), standart hata (`stderr`), milisaniye cinsinden çalışma süresi ve çıkış kodu analizi.
- **Komut Geçmişi:** Önceden çalıştırılan komutlar arasında yukarı/aşağı tuşlarıyla gezinme.

### 📦 6. Yüklü Yazılımlar & Sessiz Kaldırma (Silent Uninstall)
- Bilgisayardaki tüm 32-bit ve 64-bit yüklü programları, sürüm bilgilerini, üreticilerini ve kurulum tarihlerini listeleme.
- Arama ve filtreleme motoru.
- **Tek Tıkla Sessiz Kaldırma:** Seçilen programı arka planda kullanıcıyı rahatsız etmeden (`/qn`, `/quiet`, `/silent`, `/s` parametreleriyle) doğrudan `SYSTEM` yetkisiyle kaldırma.

### 🔄 7. Windows Güncellemeleri Takibi
- Yüklü tüm Windows KB güncelleme paketlerini, açıklamasını ve yüklenme tarihini listeleme.
- Windows Update (`wuauserv`) servis durumu denetimi ve uzaktan yeniden başlatabilme.

### 📝 8. Denetim & Aktivite Kayıtları (Audit Logging)
- Cihaz üzerinde çalıştırılan tüm komutlar, canlı masaüstü oturumları, uygulama kaldırma eylemleri ve güç yönetimi hareketleri veritabanında denetim kaydı (`CommandAudits`) olarak saklanır.

### 🚀 9. Otomatik Güncelleme (OTA Self-Update)
- Hem Windows Servisi hem de Teknisyen Uygulaması her açılışında `/api/updates/check` adresinden güncel sürümü sorgular.
- Web panelinden tek tıkla seçili cihaza **Uzaktan Sessiz Güncelleme** sinyali gönderme imkanı.

---

## 🏛️ Değiştirilemez Ajan Ana Yasası (4 Temel Madde)

NexMote istemci mimarisinde aşağıdaki 4 kuraldan **asla taviz verilmez**:

| No | Madde | Kural Açıklaması |
| :---: | :--- | :--- |
| **1** | **Windows Açılışında Otomatik Başlama** | Bilgisayar yeniden başlatıldığında veya herhangi bir kullanıcı oturum açtığında Ajan hiçbir kullanıcı müdahalesine gerek kalmadan arka planda anında devreye girer (`LocalSystem` servisi + `CreateProcessAsUser`). |
| **2** | **Sessiz Sistem Tepsisi (Tray)** | Ajan açılışta ekrana form veya açılır pencere fırlatmaz; sağ alttaki Sistem Tepsisinde (Notification Tray) yeşil kalkan simgesiyle sessizce yer alır. Yalnızca simgeye tıklandığında modern Durum Paneli açılır. |
| **3** | **Kurulum Biter Bitmez Anında Aktivasyon** | MSI veya EXE kurulumu bittiği saniye bilgisayarı yeniden başlatmaya gerek kalmadan servis ve tepsi ajanı hemen çalışır, web panelinde cihaz anında "Çevrimiçi" olur. |
| **4** | **Her Açılışta Güncelleme Kontrolü** | Ajan ve Teknisyen uygulaması her açılışında sunucudaki `/api/updates/check` endpoint'ini kontrol eder; yeni sürüm varsa arka planda sessizce güncellenir. |

---

## 🛠️ Kullanılan Yapı ve Teknolojiler

### 🌐 Backend & API Sunucusu
- **Framework:** .NET 8 (C# 12) - ASP.NET Core Minimal API
- **Gerçek Zamanlı İletişim:** SignalR WebSocket Hubs (`/hubs/signaling`)
- **Veritabanı & ORM:** SQLite + Entity Framework Core 8
- **Güvenlik & Auth:** Admin JWT Bearer Token Filtresi (`AdminAuthFilter`), Cihaza Özel 32-Bayt Kriptografik `AgentToken`, Sunucu `EnrollmentKey`
- **İşletim Sistemi Desteği:** Linux (Ubuntu 24.04 LTS / Systemd / Nginx) ve Windows Server

### 💻 Frontend & Web Teknisyen Konsolu
- **Framework & Dil:** React 18 + TypeScript + Vite
- **Stil & Tasarım Sistemi:** Vanilla CSS (CSS Variables, Enterprise Dark/Light uyumlu, Glassmorphism)
- **İkonografi:** Lucide React
- **Performans:** Zero-dependency UI mimarisi, optimize edilmiş DOM renderlama

### 🖥️ Windows İstemcileri (Agent & Tray)
- **Windows Servisi:** .NET 8 Windows Background Service (`LocalSystem`), 20sn Heartbeat, `CpuUsageSampler` (10dk kayan pencere gerçek CPU ölçümü)
- **Oturum Yöneticisi:** `WTSQuerySessionInformation`, `CreateProcessAsUser`, `DuplicateTokenEx` (Kullanıcı masaüstüne süreç enjeksiyonu)
- **Sistem Tepsisi (Tray):** Windows Forms .NET 8, modern antivirüs tarzı Durum Paneli (`DashboardForm`)
- **Ekran Yakalama Motoru:** GDI+ `BitBlt` & DirectX DXGI Desktop Duplication
- **Girdi Enjeksiyonu:** Win32 API `SendInput`, `mouse_event`, `keybd_event`, Named Pipe IPC

### 🎯 Teknisyen Masaüstü Uygulaması
- **Framework:** WPF (Windows Presentation Foundation) .NET 8 (C# 12)
- **Görüntü İşleme:** Gerçek zamanlı çoklu monitör viewport renderlama, akıllı koordinat haritalama
- **Protokol:** Özel URL Protokol İşleyicisi (`nexmote://sessionId/deviceId/token`)

### 📦 Paketleme & Dağıtım Motoru
- **Kurumsal MSI Yükleyici:** WiX Toolset v4 (Per-Machine, UAC yetkilendirmeli, Windows Servis kayıtlı)
- **Ultra Hızlı EXE Yükleyici:** Inno Setup (1.5 saniyede ultra hızlı ve sessiz kurulum)

---

## 🏗️ Sistem Mimarisi ve Veri Akışı

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

## 🧱 Klasör Yapısı ve Modüller

```
NexMote/
├── AGENTS.md                 # Master AI Geliştirici, Proje Mimarı & Ana Yasa Dokümanı
├── CHANGELOG.md              # Sürüm Günlüğü & Değişiklik Tarihçesi
├── README.md                 # Proje Kapsamlı Dokümantasyonu
├── NexMote.sln               # Ana Visual Studio Çözüm Dosyası (.NET 8)
├── src/                      # .NET 8 Kaynak Kodları
│   ├── NexMote.Api/          # ASP.NET Core 8 Web API & SignalR Sunucusu
│   │   ├── Auth/             # Bearer Token AdminAuthFilter
│   │   ├── Data/             # AppDbContext (Entity Framework Core SQLite)
│   │   ├── Hubs/             # SignalingHub.cs (/hubs/signaling Canlı Akış Rölesi)
│   │   ├── Services/         # DeviceRegistry, DownloadCatalog, RemoteSessionRegistry
│   │   └── wwwroot/          # Vite Tarafından Üretilen React Statik Dosyaları
│   ├── NexMote.Agent.Windows/# Windows Background Service (LocalSystem Servisi)
│   ├── NexMote.Agent.Tray/   # Kullanıcı Oturumu Ekran Yayını & Durum Paneli
│   ├── NexMote.TechnicianApp/# Teknisyen WPF Masaüstü Uygulaması
│   ├── NexMote.Cleaner/      # Temiz Kaldırma ve Servis Sıfırlama Aracı
│   └── NexMote.Shared/       # Ortak Tip ve Kontrat Kütüphanesi
│       ├── Contracts/        # Auth, Agent, Session, Streaming Veri Modelleri
│       ├── Network/          # NexMoteHttp (DNS gecikme korumalı soket yöneticisi)
│       └── Telemetry/        # SystemTelemetry & SessionUserResolver
├── web/                      # React 18 + TypeScript + Vite Web Teknisyen Konsolu
│   └── src/
│       ├── App.tsx           # Ana UI (Cihaz Listesi, Donanım Detayları, Terminal, Güncellemeler)
│       ├── api.ts            # REST API Fetch Kontratları ve DTO Tipleri
│       └── styles.css        # Vanilla CSS Enterprise Tasarım Sistemi
├── scripts/                  # Yükleyici Derleme Betikleri
│   ├── package-windows.ps1   # Tek tıkla publish + Inno Setup (.exe) + WiX (.msi) üretici
│   ├── agent-setup.iss       # Inno Setup Ajan konfigürasyonu
│   └── technician-setup.iss  # Inno Setup Teknisyen konfigürasyonu
├── assets/                   # Uygulama İkonları (nexmote.ico, nexmote.png)
└── downloads/                # Dağıtım Paketleri ve versions.json
```

---

## 🛠️ Kurulum, Paketleme ve Dağıtım

### 1. Web Konsolunu Geliştirme Ortamında Çalıştırma
```powershell
cd web
npm install
npm run dev
```

### 2. .NET Solution Derleme
```powershell
.\.dotnet\dotnet.exe build NexMote.sln -c Release
```

### 3. Windows Yükleyicilerini Paketleme (EXE + MSI)
```powershell
powershell -ExecutionPolicy Bypass -File scripts\package-windows.ps1 -ServerUrl "https://nexmote.com" -EnrollmentKey "your-key" -Version "0.6.3"
```
*Bu betik `artifacts/package/` altına dosyaları derler, Inno Setup ile ultra hızlı `NexMote-Agent-Setup.exe` ve WiX ile kurumsal `NexMote-Agent-Setup.msi` paketlerini üretir.*

### 4. Canlı Sunucuya Yayınlama (Linux VPS)
```powershell
# Linux binary publish ve SCP transfer
.\.dotnet\dotnet.exe publish src/NexMote.Api/NexMote.Api.csproj -c Release -r linux-x64 --self-contained false -o ./publish-linux
Copy-Item -Recurse -Force 'web\dist\*' 'publish-linux\wwwroot\'
Compress-Archive -Path 'publish-linux\*' -DestinationPath 'publish-linux.zip' -Force
scp -i "$env:USERPROFILE\.ssh\id_ed25519" publish-linux.zip root@186.241.21.133:/tmp/
ssh -i "$env:USERPROFILE\.ssh\id_ed25519" root@186.241.21.133 'unzip -o /tmp/publish-linux.zip -d /var/www/nexmote/ && systemctl restart nexmote.service'
```

---

## 🔒 Güvenlik Mimarisi

- **Uçtan Uca Şifreleme:** Tüm REST ve SignalR trafiği HTTPS / WSS (TLS 1.3) üzerinden akar.
- **Admin İzolasyonu:** Cihaz yönetimi, oturum başlatma ve ayarlar `AdminAuthFilter` ile korunan Bearer token gerektirir.
- **Ajan Doğrulaması:** Ajan sunucuya ilk kayıtta gizli `EnrollmentKey` kullanır; sonrasında sunucunun ürettiği benzersiz 32-baytlık `AgentToken` ile periyodik doğrulanır.
- **Güvenli Sır Yönetimi:** API anahtarları ve kayıt şifreleri kaynak kodda saklanmaz; sunucuda ortam değişkenleri (`Environment=`) üzerinden yönetilir.

---

## 📄 Lisans

Bu proje kurumsal kullanım ve özel dağıtım amaçlı tescilli lisans altında geliştirilmektedir. Tüm hakları saklıdır.

