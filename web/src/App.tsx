import {
  CheckCircle2,
  Cpu,
  Download,
  ExternalLink,
  Globe,
  Key,
  Laptop,
  Monitor,
  PlugZap,
  RefreshCw,
  Save,
  Search,
  Server,
  Settings,
  ShieldCheck,
  Wifi,
  WifiOff
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import {
  createRemoteSession,
  DeviceSummary,
  DownloadPackage,
  generatePackages,
  getServerSettings,
  listDevices,
  listDownloads,
  ServerSettings,
  updateServerSettings
} from "./api";

type View = "devices" | "downloads" | "settings";
type StatusFilter = "all" | "online" | "offline";

export function App() {
  const [devices, setDevices] = useState<DeviceSummary[]>([]);
  const [downloads, setDownloads] = useState<DownloadPackage[]>([]);
  const [settings, setSettings] = useState<ServerSettings>({
    serverUrl: "http://127.0.0.1:5080",
    enrollmentKey: "dev-enrollment-key",
    heartbeatSeconds: 20,
    defaultLocationCode: "OFFICE"
  });

  const [view, setView] = useState<View>("devices");
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [status, setStatus] = useState("Hazır");
  const [loading, setLoading] = useState(false);
  const [savingSettings, setSavingSettings] = useState(false);
  const [generatingPackages, setGeneratingPackages] = useState(false);
  const [connectingId, setConnectingId] = useState<string | null>(null);

  async function refresh() {
    setLoading(true);
    setStatus("Cihazlar güncelleniyor...");
    try {
      setDevices(await listDevices());
      setStatus("Cihaz listesi güncel");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Beklenmeyen hata");
    } finally {
      setLoading(false);
    }
  }

  async function refreshDownloads() {
    try {
      setDownloads(await listDownloads());
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "İndirme kataloğu alınamadı");
    }
  }

  async function loadSettings() {
    try {
      const data = await getServerSettings();
      setSettings(data);
    } catch (error) {
      console.error("Ayarlar yüklenemedi", error);
    }
  }

  async function handleSaveSettings(e: React.FormEvent) {
    e.preventDefault();
    setSavingSettings(true);
    setStatus("Sunucu ayarları kaydediliyor...");
    try {
      const updated = await updateServerSettings(settings);
      setSettings(updated);
      setStatus("Sunucu ayarları başarıyla kaydedildi.");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Ayarlar kaydedilemedi.");
    } finally {
      setSavingSettings(false);
    }
  }

  async function handleGeneratePackages() {
    setGeneratingPackages(true);
    setStatus("Agent ve Teknisyen paketleri güncel sunucu IP adresiyle yeniden derleniyor...");
    try {
      const res = await generatePackages(settings);
      setStatus(res.message);
      await refreshDownloads();
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Paket üretilemedi.");
    } finally {
      setGeneratingPackages(false);
    }
  }

  function autoDetectLanIp() {
    const currentHost = window.location.hostname;
    if (currentHost && currentHost !== "localhost" && currentHost !== "127.0.0.1") {
      setSettings((prev) => ({ ...prev, serverUrl: `http://${currentHost}:5080` }));
      setStatus(`Sunucu adresi ${currentHost} olarak ayarlandı.`);
    } else {
      setStatus("Sunucu IP adresi olarak tarayıcı adresi alındı.");
    }
  }

  async function connect(device: DeviceSummary) {
    setConnectingId(device.id);
    setStatus(`${device.deviceName} için bağlantı hazırlanıyor...`);
    try {
      const session = await createRemoteSession(device.id);
      window.location.href = session.launchUri;
      setStatus("NexMote Technician App başlatılıyor...");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Bağlantı başlatılamadı");
    } finally {
      setConnectingId(null);
    }
  }

  useEffect(() => {
    refresh();
    refreshDownloads();
    loadSettings();
    const timer = window.setInterval(refresh, 10000);
    return () => window.clearInterval(timer);
  }, []);

  const onlineCount = useMemo(() => devices.filter((d) => d.isOnline).length, [devices]);
  const offlineCount = useMemo(() => devices.filter((d) => !d.isOnline).length, [devices]);

  const filteredDevices = useMemo(() => {
    let result = devices;

    if (statusFilter === "online") {
      result = result.filter((d) => d.isOnline);
    } else if (statusFilter === "offline") {
      result = result.filter((d) => !d.isOnline);
    }

    const term = query.trim().toLowerCase();
    if (!term) {
      return result;
    }

    return result.filter((device) =>
      [
        device.deviceName,
        device.domainName,
        device.activeUser,
        device.ipAddress,
        device.locationCode,
        device.operatingSystem
      ]
        .filter(Boolean)
        .some((value) => value!.toLowerCase().includes(term))
    );
  }, [devices, query, statusFilter]);

  return (
    <main className="shell">
      {/* Sidebar Navigation */}
      <aside className="sidebar">
        <div>
          <div className="brand">
            <div className="brand-icon">
              <Monitor size={24} />
            </div>
            <div className="brand-text">
              <strong>NexMote</strong>
              <span>Teknisyen Konsolu</span>
            </div>
          </div>

          <nav>
            <button
              className={view === "devices" ? "navItem active" : "navItem"}
              type="button"
              onClick={() => setView("devices")}
            >
              <Laptop size={18} />
              Cihaz Yönetimi
            </button>
            <button
              className={view === "downloads" ? "navItem active" : "navItem"}
              type="button"
              onClick={() => setView("downloads")}
            >
              <Download size={18} />
              İndirme Merkezi
            </button>
            <button
              className={view === "settings" ? "navItem active" : "navItem"}
              type="button"
              onClick={() => setView("settings")}
            >
              <Settings size={18} />
              Sunucu Ayarları
            </button>
          </nav>
        </div>

        <div className="sidebar-footer">
          <div className="system-badge">
            <span className="live-indicator">
              <span className="pulse-dot"></span> Canlı İzleme
            </span>
            <ShieldCheck size={16} />
          </div>
        </div>
      </aside>

      {/* Main View Area */}
      <section className="content">
        {view === "devices" && (
          <>
            <header className="topbar">
              <div className="topbar-info">
                <h1>Cihaz Envanteri</h1>
                <p>Kayıtlı cihazlar, anlık online durumları ve uzaktan oturum başlatma.</p>
              </div>
              <div className="topbar-actions">
                <button
                  className="iconButton"
                  type="button"
                  onClick={refresh}
                  title="Listeyi Yenile"
                  disabled={loading}
                >
                  <RefreshCw size={18} className={loading ? "spinning" : ""} />
                </button>
              </div>
            </header>

            {/* Stats Overview Widgets */}
            <div className="stats-grid">
              <div className="stat-card">
                <div>
                  <div className="stat-label">Toplam Cihaz</div>
                  <div className="stat-value">{devices.length}</div>
                </div>
                <div className="stat-icon-wrap stat-blue">
                  <Server size={22} />
                </div>
              </div>

              <div className="stat-card">
                <div>
                  <div className="stat-label">Online Cihazlar</div>
                  <div className="stat-value">{onlineCount}</div>
                </div>
                <div className="stat-icon-wrap stat-green">
                  <Wifi size={22} />
                </div>
              </div>

              <div className="stat-card">
                <div>
                  <div className="stat-label">Offline Cihazlar</div>
                  <div className="stat-value">{offlineCount}</div>
                </div>
                <div className="stat-icon-wrap stat-gray">
                  <WifiOff size={22} />
                </div>
              </div>
            </div>

            {/* Filter and Search Bar */}
            <div className="toolbar">
              <div className="search-box">
                <Search size={18} />
                <input
                  value={query}
                  onChange={(e) => setQuery(e.target.value)}
                  placeholder="Cihaz, kullanıcı, IP veya lokasyon ile ara..."
                />
              </div>

              <div className="filter-tabs">
                <button
                  className={statusFilter === "all" ? "filter-tab active" : "filter-tab"}
                  type="button"
                  onClick={() => setStatusFilter("all")}
                >
                  Tümü ({devices.length})
                </button>
                <button
                  className={statusFilter === "online" ? "filter-tab active" : "filter-tab"}
                  type="button"
                  onClick={() => setStatusFilter("online")}
                >
                  Online ({onlineCount})
                </button>
                <button
                  className={statusFilter === "offline" ? "filter-tab active" : "filter-tab"}
                  type="button"
                  onClick={() => setStatusFilter("offline")}
                >
                  Offline ({offlineCount})
                </button>
              </div>

              <div className="status-feed">{status}</div>
            </div>

            {/* Devices Table */}
            <div className="tableWrap">
              <table>
                <thead>
                  <tr>
                    <th>Durum</th>
                    <th>Cihaz / OS</th>
                    <th>Domain</th>
                    <th>Aktif Kullanıcı</th>
                    <th>IP Adresi</th>
                    <th>Lokasyon</th>
                    <th>Agent Sürümü</th>
                    <th>İşlem</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredDevices.map((device) => (
                    <tr key={device.id} className="device-row">
                      <td>
                        <span className={device.isOnline ? "badge online" : "badge offline"}>
                          <span className="pulse-dot" style={{ display: device.isOnline ? "inline-block" : "none" }}></span>
                          {device.isOnline ? "Online" : "Offline"}
                        </span>
                      </td>
                      <td>
                        <div className="device-name-group">
                          <strong>{device.deviceName}</strong>
                          <small>{device.operatingSystem}</small>
                        </div>
                      </td>
                      <td>{device.domainName}</td>
                      <td>{device.activeUser ?? "-"}</td>
                      <td>{device.ipAddress ?? "-"}</td>
                      <td>{device.locationCode ?? "-"}</td>
                      <td>{device.agentVersion}</td>
                      <td>
                        <button
                          className="connectButton"
                          type="button"
                          disabled={!device.isOnline || connectingId === device.id}
                          onClick={() => connect(device)}
                        >
                          <ExternalLink size={15} />
                          {connectingId === device.id ? "Açılıyor..." : "Bağlan"}
                        </button>
                      </td>
                    </tr>
                  ))}

                  {filteredDevices.length === 0 && (
                    <tr>
                      <td colSpan={8}>
                        <div className="empty-state">
                          <Laptop size={36} />
                          <p>
                            {query || statusFilter !== "all"
                              ? "Arama kriterlerine uygun cihaz bulunamadı."
                              : "Henüz kayıtlı cihaz yok. NexMote Agent kurulduğunda burada görüntülenecektir."}
                          </p>
                        </div>
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        )}

        {view === "downloads" && (
          <>
            <header className="topbar">
              <div className="topbar-info">
                <h1>İndirme Merkezi</h1>
                <p>
                  Aktif Sunucu Adresi: <strong>{settings.serverUrl}</strong>
                </p>
              </div>
              <div className="topbar-actions">
                <button
                  className="connectButton"
                  type="button"
                  onClick={handleGeneratePackages}
                  disabled={generatingPackages}
                >
                  <RefreshCw size={15} className={generatingPackages ? "spinning" : ""} />
                  {generatingPackages ? "Paketler Üretiliyor..." : "Bu IP ile Paketleri Yenile"}
                </button>
              </div>
            </header>

            <div className="downloadGrid">
              {downloads.map((item) => (
                <div className="downloadItem" key={item.id}>
                  <div className="downloadIcon">
                    {item.id.startsWith("agent") ? <PlugZap size={24} /> : <Monitor size={24} />}
                  </div>

                  <div className="downloadBody">
                    <div className="downloadHeader">
                      <strong>{item.name}</strong>
                      <span className="langBadge">{item.language}</span>
                    </div>

                    <p>{item.description}</p>

                    <dl className="downloadMeta">
                      <div className="meta-item">
                        <dt>Dosya</dt>
                        <dd>{item.fileName}</dd>
                      </div>
                      <div className="meta-item">
                        <dt>Boyut</dt>
                        <dd>{item.exists ? formatBytes(item.sizeBytes) : "-"}</dd>
                      </div>
                      <div className="meta-item">
                        <dt>Sunucu Adresi</dt>
                        <dd>{settings.serverUrl}</dd>
                      </div>
                    </dl>

                    <a
                      className={item.exists ? "downloadButton" : "downloadButton disabled"}
                      href={item.exists ? item.url : undefined}
                    >
                      <Download size={15} />
                      {item.exists ? "Paketi İndir" : "Paket Hazır Değil"}
                    </a>
                  </div>
                </div>
              ))}
            </div>
          </>
        )}

        {view === "settings" && (
          <>
            <header className="topbar">
              <div className="topbar-info">
                <h1>Sunucu Ayarları</h1>
                <p>Agent'ların sunucuya erişeceği IP adresi, port ve güvenlik anahtarları.</p>
              </div>
            </header>

            <form onSubmit={handleSaveSettings} className="settings-form-container">
              <div className="settings-card">
                <div className="form-group">
                  <label>
                    <Globe size={18} />
                    <span>Sunucu Adresi / IP (Server URL)</span>
                  </label>
                  <div className="input-with-action">
                    <input
                      type="text"
                      value={settings.serverUrl}
                      onChange={(e) => setSettings({ ...settings, serverUrl: e.target.value })}
                      placeholder="http://192.168.0.104:5080"
                      required
                    />
                    <button type="button" className="btn-secondary" onClick={autoDetectLanIp}>
                      Tarayıcı IP'sini Kullan
                    </button>
                  </div>
                  <small>Agent'ların ve Teknisyen uygulamasının sunucuya bağlanacağı adrestir.</small>
                </div>

                <div className="form-group">
                  <label>
                    <Key size={18} />
                    <span>Kayıt Anahtarı (Enrollment Secret Key)</span>
                  </label>
                  <input
                    type="text"
                    value={settings.enrollmentKey}
                    onChange={(e) => setSettings({ ...settings, enrollmentKey: e.target.value })}
                    placeholder="dev-enrollment-key"
                    required
                  />
                  <small>Agent ilk kez kayıt olurken doğrulama için kullanılan gizli anahtar.</small>
                </div>

                <div className="form-actions">
                  <button type="submit" className="connectButton" disabled={savingSettings}>
                    <Save size={16} />
                    {savingSettings ? "Kaydediliyor..." : "Ayarları Kaydet"}
                  </button>

                  <button
                    type="button"
                    className="btn-secondary"
                    onClick={handleGeneratePackages}
                    disabled={generatingPackages}
                  >
                    <RefreshCw size={16} className={generatingPackages ? "spinning" : ""} />
                    {generatingPackages ? "Agent Üretiliyor..." : "Bu Ayarlarla Agent ZIP Üret"}
                  </button>
                </div>

                {status && <div className="status-banner">{status}</div>}
              </div>
            </form>
          </>
        )}
      </section>
    </main>
  );
}

function formatBytes(value: number) {
  if (value < 1024) {
    return `${value} B`;
  }
  if (value < 1024 * 1024) {
    return `${(value / 1024).toFixed(1)} KB`;
  }
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}
