# 📋 NexMote Web Konsolu — UX İyileştirme Planı

**Tarih:** 21 Ağustos 2026  
**Kapsam:** Web Teknisyen Konsolu (`web/src/`)  
**Hedef:** Kullanıcı deneyimini iyileştirme, bakım kolaylığı sağlama ve gelecek özelliklere zemin hazırlama  

---

## 📊 Mevcut Durum Özeti

| Bileşen | Satır Sayısı | Durum |
|---------|-------------|-------|
| `App.tsx` | ~1876 | Tek dosya, tüm UI burada |
| `styles.css` | ~2089 | Tek dosya, tüm stiller burada |
| `api.ts` | ~280 | API katmanı, temiz |
| `main.tsx` | ~10 | giriş noktası |

**Teknoloji:** React 19 + TypeScript + Vite 6 + lucide-react

---

## 🎯 Öncelik Sıralaması

### 🔴 Kritik (P0) — Hemen yapılmalı

---

### P0-1: App.tsx Bileşen Ayırma Refactor

**Sorun:** 1876 satırlık tek dosya — bakım ve geliştirme zorlaştırıyor.

**Ayrılması gereken bileşenler:**

```
web/src/
├── App.tsx                    # Ana layout ve state yönetimi (~400 satır)
├── components/
│   ├── LoginScreen.tsx        # Giriş ekranı (~180 satır)
│   ├── NavigationRail.tsx     # Sol kenar çubuğu (~60 satır)
│   ├── AppHeader.tsx          # Üst başlık + arama + bildirimler (~120 satır)
│   ├── DeviceTable.tsx        # Cihaz listesi tablosu (~300 satır)
│   ├── DeviceDetailPage.tsx   # Cihaz detay sayfası (~200 satır)
│   ├── DeviceOverview.tsx     # Genel Bakış sekmesi (~100 satır)
│   ├── DevicePerformance.tsx  # Performans sekmesi (~80 satır)
│   ├── DeviceNetwork.tsx      # Ağ & Bağdaştırıcılar sekmesi (~150 satır)
│   ├── DeviceApplications.tsx # Yüklü Uygulamalar sekmesi (~120 satır)
│   ├── WebTerminal.tsx        # Terminal sekmesi (~350 satır)
│   ├── ActivityLog.tsx        # Aktivite & Denetim sekmesi (~60 satır)
│   ├── DownloadsView.tsx      # İndirmeler sayfası (~80 satır)
│   ├── SettingsView.tsx       # Sunucu ayarları sayfası (~100 satır)
│   ├── DeleteDeviceModal.tsx  # Silme onay modalı (~120 satır)
│   ├── BulkActionBar.tsx      # Toplu işlem çubuğu (~50 satır)
│   └── Toast.tsx              # Bildirim toast'u (~20 satır)
├── hooks/
│   ├── useDevices.ts          # Cihaz verisi ve polling mantığı
│   ├── useAuth.ts             # Kimlik doğrulama state ve fonksiyonları
│   ├── useTerminal.ts         # Terminal state ve komut çalıştırma
│   └── useActivityLog.ts      # Aktivite günlüğü state
├── api.ts                     # Mevcut API katmanı (değişiklik yok)
├── types.ts                   # Tüm TypeScript tipleri (api.ts'den taşınacak)
├── utils.ts                   # Yardımcı fonksiyonlar (formatLastSeen, cleanUserName, vb.)
├── styles.css                 # Mevcut stiller (değişiklik yok)
└── main.tsx                   # Giriş noktası (değişiklik yok)
```

**Beklenen fayda:** Her bileşen 50-350 satır aralığında olacak, bağımsız geliştirilebilir ve test edilebilir olacak.

**Yaklaşım:**
1. `types.ts` ve `utils.ts` oluştur (api.ts'den tipleri, App.tsx'ten yardımcı fonksiyonları taşı)
2. `hooks/` klasörünü oluştur (state mantığını App.tsx'ten çıkar)
3. Her bileşeni ayrı dosyaya taşı (en küçük ve bağımsız olanlardan başla)
4. App.tsx'i sadece layout ve state orchestration olarak bırak

---

### P0-2: Aktivite & Denetim Sekmesine Komut Geçmişi Ekleme

**Sorun:** "Aktivite & Denetim" sekmesi şu an boş — `CommandAudits` tablosundan veri çekip göstermiyor.

**Çözüm:**
- Backend'de `GET /api/devices/{id}/command-audits` endpoint'i ekle
- Web terminalinde çalıştırılan komutların geçmişini bu sekmede göster
- Her satır: zaman, shell türü, komut, çıkış kodu, süre, çıktı önizleme
- Sıralama ve filtreleme desteği

**Backend değişikliği:**
```csharp
// NexMote.Api/Program.cs — admin grubuna ekle
admin.MapGet("/devices/{id:guid}/command-audits", (Guid id, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    var audits = db.CommandAudits
        .Where(a => a.DeviceId == id)
        .OrderByDescending(a => a.ExecutedAt)
        .Take(100)
        .ToList();
    return Results.Ok(audits);
});
```

**Frontend değişikliği:**
```tsx
// DeviceActivity.tsx — yeni bileşen
export function DeviceActivity({ deviceId }: { deviceId: string }) {
  const [audits, setAudits] = useState<CommandAudit[]>([]);
  // GET /api/devices/{id}/command-audits ile çek
  // Tablo formatında göster: Zaman | Shell | Komut | Çıkış Kodu | Süre
}
```

---

### 🟡 Yüksek Öncelik (P1) — 1-2 hafta içinde

---

### P1-1: Klavye Kısayolları ve Erişilebilirlik

**Mevcut eksiklikler:**
- `/` kısayolu display ediliyor ama JS'te dinlenmiyor
- Terminal bölümünde aria-label eksik
- Sekme geçişleri için kısayol yok

**Eklenecekler:**

```tsx
// App.tsx'e eklenecek global keyboard handler
useEffect(() => {
  function handleKeyDown(e: KeyboardEvent) {
    // `/` ile arama kutusuna odaklan
    if (e.key === "/" && !e.ctrlKey && !e.metaKey && document.activeElement?.tagName !== "INPUT") {
      e.preventDefault();
      document.getElementById("global-search")?.focus();
    }
    // Ctrl+K ile arama (klasik Cmd+K pattern)
    if ((e.ctrlKey || e.metaKey) && e.key === "k") {
      e.preventDefault();
      document.getElementById("global-search")?.focus();
    }
    // Ctrl+1-6 ile sekme geçişi (detail view'da)
    if ((e.ctrlKey || e.metaKey) && e.key >= "1" && e.key <= "6" && view === "device-detail") {
      e.preventDefault();
      const tabs: DetailTab[] = ["overview", "performance", "network", "applications", "terminal", "activity"];
      setActiveDetailTab(tabs[parseInt(e.key) - 1]);
    }
    // Escape ile modal kapatma
    if (e.key === "Escape") {
      if (deleteModal) setDeleteModal(null);
      if (showNotifications) setShowNotifications(false);
    }
  }
  document.addEventListener("keydown", handleKeyDown);
  return () => document.removeEventListener("keydown", handleKeyDown);
}, [view, deleteModal, showNotifications]);
```

**Terminal iyileştirmeleri:**
```tsx
// Ctrl+L ile terminal temizleme
// Tab ile geçmiş komut doldurma
// Ctrl+C ile çalışan komutu iptal etme (frontend tarafında)
```

---

### P1-2: Gerçek Zamanlı Bildirimler (WebSocket)

**Mevcut durum:** 3sn'de bir tüm cihaz listesi yeniden çekiliyor.

**İyileştirme:**
```tsx
// hooks/useRealtimeNotifications.ts
import * as signalR from "@microsoft/signalr";

export function useRealtimeNotifications() {
  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl("/hubs/signaling")
      .withAutomaticReconnect()
      .build();

    connection.on("DeviceStatusChanged", (deviceId, isOnline) => {
      // Cihaz durumu değiştiğinde listeyi güncelle
      refreshDevices();
      // Bildirim göster
      addActivityLog(`${deviceId} cihazı ${isOnline ? "çevrimiçi" : "çevrimdışı"}`,
        isOnline ? "success" : "warn");
    });

    connection.on("CommandCompleted", (requestId, result) => {
      // Terminal komutu tamamlandığında
      addActivityLog(`Komut tamamlandı: ${result.command}`, "success");
    });

    connection.start();
    return () => connection.stop();
  }, []);
}
```

---

### P1-3: Terminal İyileştirmeleri

**Eklenecekler:**

| Özellik | Açıklama |
|---------|----------|
| Ctrl+L | Terminal ekranını temizle |
| Ctrl+C | Çalışan komutu iptal et |
| Çıktı kaydetme | `.txt` olarak indirme butonu |
| Büyük çıktı kısaltma | 10K+ satır → ellipsis + "Tamamını Göster" |
| Font boyutu kontrolü | Ctrl+/- ile zoom |
| Genişletme modu | Terminal penceresini tam ekran yap |

```tsx
// WebTerminal.tsx — eklenecek fonksiyonlar
function exportTerminalLog(logs: TerminalLog[]) {
  const content = logs.map(l =>
    `> ${l.command}\n${l.stdOut || l.stdErr}\n--- Çıkış Kodu: ${l.exitCode} ---`
  ).join("\n\n");
  const blob = new Blob([content], { type: "text/plain" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `terminal-${selectedDevice.deviceName}-${Date.now()}.txt`;
  a.click();
  URL.revokeObjectURL(url);
}
```

---

### 🟢 Orta Öncelik (P2) — 2-4 hafta içinde

---

### P2-1: Karanlık Mod Desteği

**Mevcut durum:** CSS variables zaten tanımlı — `--bg-app`, `--bg-surface`, `--text-main` vb.

**Uygulama:**

```css
/* styles.css — karanlık mod değişkenleri */
@media (prefers-color-scheme: dark) {
  :root {
    --bg-app: #0f172a;
    --bg-surface: #1e293b;
    --bg-subtle: #1e293b;
    --bg-hover: #334155;
    --border-color: #334155;
    --border-subtle: #1e293b;
    --text-main: #f1f5f9;
    --text-muted: #94a3b8;
    --text-dim: #64748b;
    --text-subtle: #475569;
    --primary-light: rgba(37, 99, 235, 0.15);
    --online-bg: rgba(16, 185, 129, 0.1);
    --warn-bg: rgba(245, 158, 11, 0.1);
    --danger-bg: rgba(239, 68, 68, 0.1);
  }
}
```

**Manuel geçiş desteği:**
```tsx
// hooks/useTheme.ts
type Theme = "light" | "dark" | "system";

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() =>
    (localStorage.getItem("nexmote-theme") as Theme) || "system"
  );

  useEffect(() => {
    const root = document.documentElement;
    root.classList.remove("theme-light", "theme-dark");
    if (theme === "system") {
      // prefers-color-scheme'a bırak
    } else {
      root.classList.add(`theme-${theme}`);
    }
    localStorage.setItem("nexmote-theme", theme);
  }, [theme]);

  return { theme, setTheme };
}
```

**Yer:** Ayarlar sayfasına "Tema" bölümü ekle (Açık / Koyu / Sistem)

---

### P2-2: Dashboard Özeti (Ana Sayfa KPI Kartları)

**Mevcut durum:** Cihaz listesine doğrudan geçiliyor — genel bakış yok.

**Önerilen layout:**

```
┌─────────────┬─────────────┬─────────────┬─────────────┐
│ Toplam Cihaz│ Çevrimiçi   │ Ortalama    │ Aktif       │
│     12      │     8       │ CPU: %34    │ Komut: 3    │
└─────────────┴─────────────┴─────────────┴─────────────┘
┌─────────────────────────────────────────────────────┐
│                  Cihaz Listesi (mevcut tablo)        │
└─────────────────────────────────────────────────────┘
```

```tsx
// components/DashboardSummary.tsx
export function DashboardSummary({ devices }: { devices: DeviceSummary[] }) {
  const onlineCount = devices.filter(d => d.isOnline).length;
  const avgCpu = devices.length > 0
    ? Math.round(devices.reduce((sum, d) => sum + (d.cpuUsagePercent || 0), 0) / devices.length)
    : 0;
  const warningCount = devices.filter(d =>
    latestAgentVersion && d.agentVersion !== latestAgentVersion
  ).length;

  return (
    <div className="dashboard-summary-grid">
      <SummaryCard icon={<Monitor />} label="Toplam Cihaz" value={devices.length} />
      <SummaryCard icon={<Wifi />} label="Çevrimiçi" value={onlineCount} color="green" />
      <SummaryCard icon={<Cpu />} label="Ort. CPU" value={`%${avgCpu}`} color={avgCpu > 70 ? "red" : "blue"} />
      <SummaryCard icon={<Bell />} label="Güncelleme Bekleyen" value={warningCount} color="orange" />
    </div>
  );
}
```

---

### P2-3: Cihaz Grupları ve Etiketler

**Mevcut durum:** Sadece `locationCode` var (OFFICE, LAB, vb.)

**İyileştirme:**
- Cihazlara çoklu etiket ekleme (örn: "sunucu", "test", "müşteri")
- Filtre çubuğuna etiket bazlı filtre ekleme
- Etiket renk kodlaması

**Backend değişikliği:**
```csharp
// Devices tablosuna ekle
public class DeviceRecord {
    // ... mevcut alanlar
    public string[] Tags { get; set; } = Array.Empty<string>();
}
```

---

### P2-4: Toplu Komut Çalıştırma

**Mevcut durum:** Tek cihaza komut gönderilebiliyor, toplu komut yok.

**İyileştirme:**
- Tabloda çoklu cihaz seç → "Toplu Komut" butonu
- Açılan modal'da komut yaz → seçili tüm cihazlara gönder
- Sonuçları cihaz bazında göster

```tsx
// components/BulkCommandModal.tsx
export function BulkCommandModal({ deviceIds, onClose }: Props) {
  const [command, setCommand] = useState("");
  const [results, setResults] = useState<Map<string, CommandResult>>(new Map());

  async function executeBulk() {
    for (const id of deviceIds) {
      try {
        const result = await executeDeviceCommand(id, shell, command, runAsAdmin);
        setResults(prev => new Map(prev).set(id, { status: "success", result }));
      } catch (err) {
        setResults(prev => new Map(prev).set(id, { status: "error", error: err.message }));
      }
    }
  }

  return (
    <div className="modal-dialog">
      <h3>{deviceIds.length} cihaza komut gönder</h3>
      <textarea value={command} onChange={...} />
      <button onClick={executeBulk}>Gönder</button>
      {/* Sonuçları listele */}
    </div>
  );
}
```

---

### 🔵 Düşük Öncelik (P3) — Gelecek Sprint'ler

---

### P3-1: Responsive Tasarım

**Hedef breakpoint'ler:**

```css
/* styles.css — responsive breakpoint'ler */
@media (max-width: 1024px) {
  /* Tablet: sidebar collapse, tablo kartlara dönüş */
  .nav-rail { width: 48px; }
  .filter-bar { flex-wrap: wrap; }
}

@media (max-width: 768px) {
  /* Mobil: tamamen kart tabanlı görünüm */
  .nav-rail { display: none; }
  .app-layout { flex-direction: column; }
  .op-table-container { display: none; }
  .mobile-card-list { display: block; } /* Yeni: kart listesi */
}
```

**Yeni mobil kart bileşeni:**
```tsx
// components/MobileDeviceCard.tsx
export function MobileDeviceCard({ device, onSelect }: Props) {
  return (
    <div className="mobile-device-card" onClick={() => onSelect(device.id)}>
      <div className="card-header">
        <StatusBadge online={device.isOnline} />
        <span className="device-name">{device.deviceName}</span>
      </div>
      <div className="card-body">
        <span>{device.ipAddress}</span>
        <span>{device.activeUser}</span>
        <span>CPU: %{device.cpuUsagePercent}</span>
      </div>
    </div>
  );
}
```

---

### P3-2: Sanal Liste (Virtualization)

**Sorun:** 100+ cihazda tablo satırları performansı düşürebilir.

**Çözüm:** `react-window` veya `@tanstack/react-virtual` kütüphanesi

```tsx
import { useVirtualizer } from "@tanstack/react-virtual";

function VirtualDeviceTable({ devices }: { devices: DeviceSummary[] }) {
  const parentRef = useRef<HTMLDivElement>(null);
  const virtualizer = useVirtualizer({
    count: devices.length,
    getScrollElement: () => parentRef.current,
    estimateSize: () => 38, // satır yüksekliği
  });

  return (
    <div ref={parentRef} className="op-table-container" style={{ overflow: "auto" }}>
      <div style={{ height: `${virtualizer.getTotalSize()}px`, position: "relative" }}>
        {virtualizer.getVirtualItems().map(virtualRow => (
          <div
            key={virtualRow.key}
            style={{
              position: "absolute",
              top: 0,
              transform: `translateY(${virtualRow.start}px)`,
              height: `${virtualRow.size}px`,
            }}
          >
            <DeviceTableRow device={devices[virtualRow.index]} />
          </div>
        ))}
      </div>
    </div>
  );
}
```

---

### P3-3: Uzaktan Dosya Aktarımı

**Mevcut altyapı:** SignalR Hub'ta `file-chunk` mesaj tipi zaten var.

**İyileştirme:**
- Cihaz detay sekmesine "Dosyalar" sekmesi ekle
- Dosya yükleme (technician → agent): drag-and-drop desteği
- Dosya indirme (agent → technician): cihazdaki dosyaları listeleme

---

### P3-4: Cihaz Karşılaştırma

**İyileştirme:**
- İki veya daha fazla cihazı seç → "Karşılaştır" butonu
- Yan yana performans ve donanım karşılaştırması
- Farkları vurgulama (CPU, RAM, disk, sürüm farkları)

---

### P3-5: Dışa Aktarma (Export)

**Özellikler:**
- Cihaz listesini CSV/JSON olarak dışa aktarma
- Terminal geçmişini dışa aktarma
- Sunucu ayarlarını dışa aktarma/yedekleme

```tsx
function exportDevicesToCSV(devices: DeviceSummary[]) {
  const headers = ["Cihaz Adı", "IP", "Durum", "CPU", "RAM", "Ajan Sürümü", "Son Sinyal"];
  const rows = devices.map(d => [
    d.deviceName,
    d.ipAddress || "",
    d.isOnline ? "Çevrimiçi" : "Çevrimdışı",
    `${d.cpuUsagePercent || 0}%`,
    `${((d.memoryUsedMb || 0) / 1024).toFixed(1)}/${((d.memoryTotalMb || 0) / 1024).toFixed(1)} GB`,
    d.agentVersion,
    d.lastSeenAt
  ]);
  const csv = [headers, ...rows].map(row => row.join(",")).join("\n");
  downloadFile(csv, `nexmote-devices-${Date.now()}.csv`, "text/csv");
}
```

---

## 📅 Uygulama Zaman Çizelgesi

| Hafta | Görev | Öncelik |
|-------|-------|---------|
| 1 | P0-1: Bileşen ayırma refactor | 🔴 Kritik |
| 2 | P0-2: Komut geçmişi + P1-1: Kısayollar | 🔴 Kritik |
| 3 | P1-2: Gerçek zamanlı bildirimler | 🟡 Yüksek |
| 4 | P1-3: Terminal iyileştirmeleri | 🟡 Yüksek |
| 5-6 | P2-1: Karanlık mod + P2-2: Dashboard | 🟢 Orta |
| 7-8 | P2-3: Cihaz grupları + P2-4: Toplu komut | 🟢 Orta |
| 9+ | P3-5: Responsive + Sanal Liste + Export | 🔵 Düşük |

---

## 📦 Bağımlılıklar (eklenecek npm paketleri)

| Paket | Amaç | P3'te gerekli |
|-------|------|---------------|
| `@microsoft/signalr` | Gerçek zamanlı WebSocket | P1-2 |
| `@tanstack/react-virtual` | Sanal liste | P3-2 |
| `file-saver` | Dosya indirme | P3-5 |

---

## ✅ Başarı Kriterleri

- [ ] App.tsx 500 satırın altına düşecek (refactor sonrası)
- [ ] Tüm sekmeler bağımsız test edilebilir olacak
- [ ] Klavye kısayolları çalışacak (/, Ctrl+K, Ctrl+1-6, Escape)
- [ ] Komut geçmişi Aktivite sekmesinde görünecek
- [ ] Karanlık mod desteklenecek (system preference + manuel toggle)
- [ ] Mobilde en az OKUNABİLİR bir görünüm olacak
- [ ] Lighthouse Accessibility skoru 90+ olacak

---

*Bu plan dosyası NexMote web konsolunun UX kalitesini artırmak için hazırlanmıştır. Öncelik sıralaması, uygulama zorluğu ve beklenen faydaya göre belirlenmiştir.*
