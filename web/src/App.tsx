import { Download, Monitor, PlugZap, RefreshCw, Search } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { createRemoteSession, DeviceSummary, DownloadPackage, listDevices, listDownloads } from "./api";

type View = "devices" | "downloads";

export function App() {
  const [devices, setDevices] = useState<DeviceSummary[]>([]);
  const [downloads, setDownloads] = useState<DownloadPackage[]>([]);
  const [view, setView] = useState<View>("devices");
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("Hazir");
  const [loading, setLoading] = useState(false);

  async function refresh() {
    setLoading(true);
    setStatus("Cihazlar yukleniyor");
    try {
      setDevices(await listDevices());
      setStatus("Cihaz listesi guncel");
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
      setStatus(error instanceof Error ? error.message : "Indirme katalogu alinamadi");
    }
  }

  async function connect(device: DeviceSummary) {
    setStatus(`${device.deviceName} icin baglanti hazirlaniyor`);
    try {
      const session = await createRemoteSession(device.id);
      window.location.href = session.launchUri;
      setStatus("NexMote Technician App aciliyor");
    } catch (error) {
      setStatus(error instanceof Error ? error.message : "Baglanti baslatilamadi");
    }
  }

  useEffect(() => {
    refresh();
    refreshDownloads();
    const timer = window.setInterval(refresh, 15000);
    return () => window.clearInterval(timer);
  }, []);

  const filteredDevices = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) {
      return devices;
    }

    return devices.filter((device) =>
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
  }, [devices, query]);

  return (
    <main className="shell">
      <aside className="sidebar">
        <div className="brand">
          <Monitor size={24} />
          <div>
            <strong>NexMote</strong>
            <span>Technician Console</span>
          </div>
        </div>

        <nav>
          <button className={view === "devices" ? "navItem active" : "navItem"} type="button" onClick={() => setView("devices")}>
            <Monitor size={18} />
            Cihazlar
          </button>
          <button className={view === "downloads" ? "navItem active" : "navItem"} type="button" onClick={() => setView("downloads")}>
            <Download size={18} />
            Indirilenler
          </button>
        </nav>
      </aside>

      <section className="content">
        {view === "devices" ? (
          <>
            <header className="topbar">
              <div>
                <h1>Cihaz Listesi</h1>
                <p>Domain cihazlari, online durumlari ve teknik baglanti islemleri.</p>
              </div>
              <button className="iconButton" type="button" onClick={refresh} title="Yenile" disabled={loading}>
                <RefreshCw size={18} />
              </button>
            </header>

            <div className="toolbar">
              <label className="search">
                <Search size={17} />
                <input
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  placeholder="Cihaz, kullanici, IP veya lokasyon ara"
                />
              </label>
              <span className="statusText">{status}</span>
            </div>

            <div className="tableWrap">
              <table>
                <thead>
                  <tr>
                    <th>Durum</th>
                    <th>Cihaz</th>
                    <th>Domain</th>
                    <th>Kullanici</th>
                    <th>IP</th>
                    <th>Lokasyon</th>
                    <th>Agent</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {filteredDevices.map((device) => (
                    <tr key={device.id}>
                      <td>
                        <span className={device.isOnline ? "pill online" : "pill offline"}>
                          {device.isOnline ? "Online" : "Offline"}
                        </span>
                      </td>
                      <td>
                        <strong>{device.deviceName}</strong>
                        <small>{device.operatingSystem}</small>
                      </td>
                      <td>{device.domainName}</td>
                      <td>{device.activeUser ?? "-"}</td>
                      <td>{device.ipAddress ?? "-"}</td>
                      <td>{device.locationCode ?? "-"}</td>
                      <td>{device.agentVersion}</td>
                      <td>
                        <button className="connectButton" type="button" disabled={!device.isOnline} onClick={() => connect(device)}>
                          Baglan
                        </button>
                      </td>
                    </tr>
                  ))}
                  {filteredDevices.length === 0 && (
                    <tr>
                      <td colSpan={8} className="empty">
                        Kayitli cihaz yok. NexMote Agent enroll oldugunda burada gorunecek.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </>
        ) : (
          <>
            <header className="topbar">
              <div>
                <h1>Indirilenler</h1>
                <p>Test bilgisayarlarina kurulacak NexMote Agent ve Technician App paketleri.</p>
              </div>
              <button className="iconButton" type="button" onClick={refreshDownloads} title="Yenile">
                <RefreshCw size={18} />
              </button>
            </header>

            <div className="downloadGrid">
              {downloads.map((item) => (
                <section className="downloadItem" key={item.id}>
                  <div className="downloadIcon">
                    {item.id.startsWith("agent") ? <PlugZap size={22} /> : <Monitor size={22} />}
                  </div>
                  <div className="downloadBody">
                    <div className="downloadTitle">
                      <div className="downloadHeading">
                        <strong>{item.name}</strong>
                        <span className="languageTag">{item.language}</span>
                      </div>
                      <span className={item.exists ? "pill online" : "pill offline"}>
                        {item.exists ? "Hazir" : "Paket yok"}
                      </span>
                    </div>
                    <p>{item.description}</p>
                    <dl>
                      <div>
                        <dt>Dosya</dt>
                        <dd>{item.fileName}</dd>
                      </div>
                      <div>
                        <dt>Boyut</dt>
                        <dd>{item.exists ? formatBytes(item.sizeBytes) : "-"}</dd>
                      </div>
                      <div>
                        <dt>Yetki</dt>
                        <dd>{item.requiresAdmin ? "Yonetici gerekir" : "Kullanici kurulumu"}</dd>
                      </div>
                    </dl>
                    <a className={item.exists ? "downloadButton" : "downloadButton disabled"} href={item.exists ? item.url : undefined}>
                      Indir
                    </a>
                  </div>
                </section>
              ))}
            </div>
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
