import {
  Activity,
  ArrowUpDown,
  Bell,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Clock,
  Copy,
  Cpu,
  Download,
  Eye,
  EyeOff,
  HardDrive,
  Laptop,
  Layers,
  Lock,
  LogOut,
  Monitor,
  Play,
  PlugZap,
  RefreshCw,
  Save,
  Search,
  Send,
  Server,
  Settings,
  Shield,
  ShieldCheck,
  Table as TableIcon,
  Terminal,
  User,
  Wifi,
  Zap
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import {
  checkUpdates,
  clearStoredAdminToken,
  createRemoteSession,
  DeviceSummary,
  DownloadPackage,
  getServerSettings,
  getStoredAdminToken,
  listDevices,
  listDownloads,
  login,
  ServerSettings,
  setStoredAdminToken,
  triggerAgentUpdate,
  updateServerSettings
} from "./api";

type View = "devices" | "downloads" | "settings";
type StatusFilter = "all" | "online" | "offline" | "warning";
type DetailTab = "specs" | "terminal" | "activity";
type SortField = "deviceName" | "status" | "cpu" | "lastSeen" | "agentVersion";
type SortDirection = "asc" | "desc";

export function App() {
  const [devices, setDevices] = useState<DeviceSummary[]>([]);
  const [downloads, setDownloads] = useState<DownloadPackage[]>([]);
  const [updatingDeviceId, setUpdatingDeviceId] = useState<string | null>(null);
  const [settings, setSettings] = useState<ServerSettings>({
    serverUrl: "https://nexmote.com",
    enrollmentKey: "dev-enrollment-key",
    heartbeatSeconds: 20,
    defaultLocationCode: "OFFICE"
  });

  const [view, setView] = useState<View>("devices");
  const [viewMode, setViewMode] = useState<"table" | "split">("table");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [selectedDeviceIds, setSelectedDeviceIds] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [sortField, setSortField] = useState<SortField>("lastSeen");
  const [sortDirection, setSortDirection] = useState<SortDirection>("desc");
  const [status, setStatus] = useState<string | null>(null);
  const [showNotifications, setShowNotifications] = useState(false);
  const [loading, setLoading] = useState(false);
  const [savingSettings, setSavingSettings] = useState(false);
  const [connectingId, setConnectingId] = useState<string | null>(null);
  const [latestAgentVersion, setLatestAgentVersion] = useState<string | null>(null);
  const [activeDetailTab, setActiveDetailTab] = useState<DetailTab>("specs");
  const [copiedField, setCopiedField] = useState<string | null>(null);

  // Authentication State
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(() => Boolean(getStoredAdminToken()));
  const [loginEmail, setLoginEmail] = useState("admin@nexmote.com");
  const [loginPassword, setLoginPassword] = useState("admin123");
  const [showLoginPassword, setShowLoginPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [authError, setAuthError] = useState("");
  const [isLoggingIn, setIsLoggingIn] = useState(false);

  // Live Activity Event Logs
  const [activityLogs, setActivityLogs] = useState<{ id: string; text: string; time: string; level: "info" | "success" | "warn" }[]>([]);

  // Remote Terminal Command State
  const [cmdInput, setCmdInput] = useState("");
  const [cmdLogs, setCmdLogs] = useState<string[]>([]);
  const [cmdElevated, setCmdElevated] = useState(false);

  async function handleLogin(e: React.FormEvent) {
    e.preventDefault();
    setAuthError("");
    setIsLoggingIn(true);

    try {
      const token = await login(loginEmail.trim(), loginPassword);
      setStoredAdminToken(token, rememberMe);
      setIsAuthenticated(true);
      addActivityLog("Yönetici oturumu açıldı", "success");
    } catch {
      setAuthError("E-posta veya parola hatalı. Lütfen bilgilerinizi kontrol edin.");
    } finally {
      setIsLoggingIn(false);
    }
  }

  function handleLogout() {
    clearStoredAdminToken();
    setIsAuthenticated(false);
  }

  function addActivityLog(text: string, level: "info" | "success" | "warn" = "info") {
    const time = new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    setActivityLogs(prev => [{ id: Math.random().toString(36).substring(2, 9), text, time, level }, ...prev.slice(0, 49)]);
  }

  async function refresh(isManual: boolean = false) {
    setLoading(true);
    if (isManual) showToast("Cihazlar güncelleniyor...");
    try {
      const data = await listDevices();
      setDevices(data);
      if (data.length > 0 && !selectedDeviceId) {
        setSelectedDeviceId(data[0].id);
      }
      if (isManual) showToast("Cihaz verileri güncellendi");
    } catch (error) {
      if (isManual) showToast(error instanceof Error ? error.message : "Sunucuya bağlanılamadı.");
    } finally {
      setLoading(false);
    }
  }

  async function refreshDownloads() {
    try {
      setDownloads(await listDownloads());
    } catch (error) {
      showToast(error instanceof Error ? error.message : "İndirmeler alınamadı");
    }
  }

  async function refreshSettings() {
    try {
      setSettings(await getServerSettings());
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Ayarlar alınamadı");
    }
  }

  async function refreshLatestVersion() {
    try {
      const info = await checkUpdates();
      if (info.agent?.version) {
        setLatestAgentVersion(info.agent.version);
      }
    } catch { }
  }

  useEffect(() => {
    if (!isAuthenticated) return;
    refresh();
    refreshDownloads();
    refreshSettings();
    refreshLatestVersion();

    const interval = setInterval(() => {
      refresh(false);
    }, 10000);
    return () => clearInterval(interval);
  }, [isAuthenticated]);

  function showToast(message: string) {
    setStatus(message);
    setTimeout(() => setStatus(null), 3500);
  }

  function copyToClipboard(text: string, fieldName: string) {
    navigator.clipboard.writeText(text);
    setCopiedField(fieldName);
    showToast(`${fieldName} panoya kopyalandı`);
    setTimeout(() => setCopiedField(null), 2000);
  }

  async function handleConnect(deviceId: string) {
    setConnectingId(deviceId);
    showToast("Canlı oturum başlatılıyor...");
    try {
      const session = await createRemoteSession(deviceId);
      window.location.href = session.launchUri;
      showToast("NexMote Teknisyen uygulaması açılıyor");
      addActivityLog(`Cihaza bağlantı başlatıldı (${deviceId.slice(0, 8)})`, "info");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Oturum açılamadı");
      addActivityLog(`Oturum açılamadı: ${error instanceof Error ? error.message : "Hata"}`, "warn");
    } finally {
      setConnectingId(null);
    }
  }

  async function handleUpdateAgent(deviceId: string) {
    setUpdatingDeviceId(deviceId);
    showToast("Uzaktan güncelleme komutu iletiliyor...");
    try {
      await triggerAgentUpdate(deviceId);
      showToast("Agent güncelleme emri iletildi.");
      addActivityLog(`Agent güncelleme emri iletildi (${deviceId.slice(0, 8)})`, "success");
      await refresh();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Güncelleme tetiklenemedi");
      addActivityLog(`Agent güncelleme başarısız: ${error instanceof Error ? error.message : "Hata"}`, "warn");
    } finally {
      setUpdatingDeviceId(null);
    }
  }

  async function handleBulkUpdateAgents() {
    if (selectedDeviceIds.size === 0) return;
    showToast(`${selectedDeviceIds.size} cihaza güncelleme sinyali gönderiliyor...`);
    for (const id of selectedDeviceIds) {
      try {
        await triggerAgentUpdate(id);
      } catch { }
    }
    showToast("Toplu güncelleme sinyalleri iletildi.");
    addActivityLog(`${selectedDeviceIds.size} cihaza toplu güncelleme emri gönderildi`, "success");
    setSelectedDeviceIds(new Set());
    await refresh();
  }

  async function handleSaveSettings(e: React.FormEvent) {
    e.preventDefault();
    setSavingSettings(true);
    try {
      const updated = await updateServerSettings(settings);
      setSettings(updated);
      showToast("Sunucu ayarları başarıyla kaydedildi.");
      addActivityLog("Sunucu ayarları güncellendi", "info");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Ayarlar kaydedilemedi");
    } finally {
      setSavingSettings(false);
    }
  }

  function handleSort(field: SortField) {
    if (sortField === field) {
      setSortDirection(prev => (prev === "asc" ? "desc" : "asc"));
    } else {
      setSortField(field);
      setSortDirection("asc");
    }
  }

  const filteredAndSortedDevices = useMemo(() => {
    const q = query.trim().toLowerCase();
    const result = devices.filter((d) => {
      const matchesQuery =
        !q ||
        d.deviceName.toLowerCase().includes(q) ||
        (d.ipAddress || "").toLowerCase().includes(q) ||
        (d.operatingSystem || "").toLowerCase().includes(q) ||
        (d.locationCode || "").toLowerCase().includes(q) ||
        (d.activeUser || "").toLowerCase().includes(q);

      const isWarning = Boolean(
        (latestAgentVersion && d.agentVersion !== latestAgentVersion) ||
        (d.cpuUsagePercent && d.cpuUsagePercent > 90)
      );

      if (statusFilter === "online") return matchesQuery && d.isOnline;
      if (statusFilter === "offline") return matchesQuery && !d.isOnline;
      if (statusFilter === "warning") return matchesQuery && isWarning;
      return matchesQuery;
    });

    result.sort((a, b) => {
      let cmp = 0;
      if (sortField === "deviceName") cmp = a.deviceName.localeCompare(b.deviceName);
      else if (sortField === "status") cmp = Number(b.isOnline) - Number(a.isOnline);
      else if (sortField === "cpu") cmp = (a.cpuUsagePercent || 0) - (b.cpuUsagePercent || 0);
      else if (sortField === "agentVersion") cmp = a.agentVersion.localeCompare(b.agentVersion);
      else if (sortField === "lastSeen") {
        const tA = new Date(a.lastSeenAt).getTime();
        const tB = new Date(b.lastSeenAt).getTime();
        cmp = tA - tB;
      }
      return sortDirection === "asc" ? cmp : -cmp;
    });

    return result;
  }, [devices, query, statusFilter, sortField, sortDirection, latestAgentVersion]);

  const selectedDevice = useMemo(
    () => devices.find((d) => d.id === selectedDeviceId) ?? devices[0] ?? null,
    [devices, selectedDeviceId]
  );

  const onlineCount = devices.filter((d) => d.isOnline).length;
  const warningCount = devices.filter((d) => latestAgentVersion && d.agentVersion !== latestAgentVersion).length;

  function toggleSelectAll() {
    if (selectedDeviceIds.size === filteredAndSortedDevices.length) {
      setSelectedDeviceIds(new Set());
    } else {
      setSelectedDeviceIds(new Set(filteredAndSortedDevices.map(d => d.id)));
    }
  }

  function toggleSelectDevice(id: string, e: React.MouseEvent) {
    e.stopPropagation();
    setSelectedDeviceIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  // --- LOGIN VIEW ---
  if (!isAuthenticated) {
    return (
      <div className="login-container">
        {/* Left Trust Panel */}
        <div className="login-trust-panel">
          <div className="login-trust-logo">
            <div style={{ width: 32, height: 32, borderRadius: 8, background: '#2563eb', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
              <ShieldCheck size={20} color="#fff" />
            </div>
            <span>NexMote</span>
          </div>

          <div className="login-trust-info">
            <h2 className="login-trust-heading">Kendi sunucunuzda çalışan kurumsal uzaktan yönetim.</h2>
            <div className="login-trust-item">
              <div className="status-dot online" />
              <span className="mono-text" style={{ color: '#cbd5e1' }}>{settings.serverUrl.replace(/^https?:\/\//, '')}</span>
            </div>
            <div className="login-trust-item">
              <Shield size={14} color="#94a3b8" />
              <span>TLS 1.3 · Şifrelenmiş Uçtan Uca Akış</span>
            </div>
            <div className="login-trust-item">
              <Server size={14} color="#94a3b8" />
              <span>Sunucu v0.5.4 · Canlı Sinyalleşme Aktif</span>
            </div>
          </div>

          <div style={{ fontSize: 11, color: '#64748b' }}>
            © 2026 NexMote · Tüm oturumlar denetim günlüğüne kaydedilir.
          </div>
        </div>

        {/* Right Form Panel */}
        <div className="login-form-panel">
          <div className="login-box">
            <div>
              <h1 className="login-title">Oturum Açın</h1>
              <p className="login-subtitle">Yönetici kimlik bilgilerinizle konsola bağlanın.</p>
            </div>

            <form onSubmit={handleLogin} style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
              {authError && (
                <div className="login-error-text">
                  {authError}
                </div>
              )}

              <div className="form-group">
                <label className="form-label">E-Posta Adresi</label>
                <div className="form-input-wrapper">
                  <input
                    type="email"
                    className="form-input"
                    placeholder="admin@nexmote.com"
                    value={loginEmail}
                    onChange={(e) => setLoginEmail(e.target.value)}
                    required
                  />
                </div>
              </div>

              <div className="form-group">
                <label className="form-label">Parola</label>
                <div className="form-input-wrapper">
                  <input
                    type={showLoginPassword ? "text" : "password"}
                    className="form-input"
                    placeholder="••••••••"
                    value={loginPassword}
                    onChange={(e) => setLoginPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    style={{ position: 'absolute', right: 10, background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                    onClick={() => setShowLoginPassword(!showLoginPassword)}
                    title={showLoginPassword ? "Gizle" : "Göster"}
                  >
                    {showLoginPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginTop: 2 }}>
                <label style={{ display: 'flex', alignItems: 'center', gap: 6, fontSize: 12.5, color: '#475569', cursor: 'pointer' }}>
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                  />
                  Oturumu açık tut
                </label>
              </div>

              <button
                type="submit"
                className="btn-primary"
                style={{ height: 38, fontSize: 13, marginTop: 6 }}
                disabled={isLoggingIn}
              >
                {isLoggingIn ? "Doğrulanıyor..." : "Giriş Yap"}
              </button>
            </form>
          </div>
        </div>
      </div>
    );
  }

  // --- MAIN AUTHENTICATED APP ---
  return (
    <div className="app-layout">
      {/* 1. Navigation Rail */}
      <aside className="nav-rail">
        <div className="rail-top">
          <div className="rail-logo" title="NexMote RMM">
            <ShieldCheck size={20} />
          </div>

          <button
            className={`rail-btn ${view === "devices" ? "active" : ""}`}
            onClick={() => setView("devices")}
            title="Cihazlar"
          >
            <Monitor size={18} />
          </button>

          <button
            className={`rail-btn ${view === "downloads" ? "active" : ""}`}
            onClick={() => setView("downloads")}
            title="İndirmeler"
          >
            <Download size={18} />
          </button>

          <button
            className={`rail-btn ${view === "settings" ? "active" : ""}`}
            onClick={() => setView("settings")}
            title="Sunucu Ayarları"
          >
            <Settings size={18} />
          </button>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 8 }}>
          <button
            className="rail-btn"
            onClick={handleLogout}
            title="Oturumu Kapat"
          >
            <LogOut size={16} />
          </button>
          <div className="rail-avatar" title={loginEmail}>
            {loginEmail.charAt(0).toUpperCase()}
          </div>
        </div>
      </aside>

      {/* 2. App Main Content */}
      <div className="app-main">
        {/* Top Header */}
        <header className="app-header">
          <div className="header-left">
            <div className="header-title">
              <span>NexMote</span>
              <span className="header-subtitle-count">
                {devices.length} cihaz · {onlineCount} çevrimiçi
                {warningCount > 0 && ` · ${warningCount} güncelleme`}
              </span>
            </div>
          </div>

          <div className="header-center">
            <div className="header-search">
              <Search size={14} className="search-icon" />
              <input
                id="global-search"
                type="text"
                placeholder="Cihaz adı, IP, kullanıcı veya lokasyon ara..."
                value={query}
                onChange={(e) => setQuery(e.target.value)}
              />
              <span className="search-shortcut">/</span>
            </div>
          </div>

          <div className="header-right">
            <button
              className="icon-action-btn"
              onClick={() => refresh(true)}
              title="Yenile"
              disabled={loading}
            >
              <RefreshCw size={15} className={loading ? "animate-spin" : ""} />
            </button>

            <button
              className="icon-action-btn"
              onClick={() => setShowNotifications(!showNotifications)}
              title="Aktivite Günlüğü"
            >
              <Bell size={15} />
              {activityLogs.length > 0 && <span className="notification-badge-dot" />}
            </button>

            <div className="user-profile-badge">
              <div className="user-avatar-mini">{loginEmail.charAt(0).toUpperCase()}</div>
              <span className="user-name">{loginEmail.split('@')[0]}</span>
            </div>
          </div>
        </header>

        {/* View 1: Device Management Console */}
        {view === "devices" && (
          <>
            {/* Filter & View Mode Bar */}
            <div className="filter-bar">
              <div className="filter-group">
                <button
                  className={`filter-btn ${statusFilter === "all" ? "active" : ""}`}
                  onClick={() => setStatusFilter("all")}
                >
                  Tümü ({devices.length})
                </button>
                <button
                  className={`filter-btn ${statusFilter === "online" ? "active" : ""}`}
                  onClick={() => setStatusFilter("online")}
                >
                  Çevrimiçi ({onlineCount})
                </button>
                <button
                  className={`filter-btn ${statusFilter === "offline" ? "active" : ""}`}
                  onClick={() => setStatusFilter("offline")}
                >
                  Çevrimdışı ({devices.length - onlineCount})
                </button>
                <button
                  className={`filter-btn ${statusFilter === "warning" ? "active" : ""}`}
                  onClick={() => setStatusFilter("warning")}
                >
                  Dikkat ({warningCount})
                </button>
              </div>

              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <button
                  className={`view-toggle-btn ${viewMode === "table" ? "active" : ""}`}
                  onClick={() => setViewMode("table")}
                  title="Operasyon Tablosu"
                >
                  <TableIcon size={14} /> Tablo
                </button>
                <button
                  className={`view-toggle-btn ${viewMode === "split" ? "active" : ""}`}
                  onClick={() => setViewMode("split")}
                  title="Detaylı Bölünmüş Görünüm"
                >
                  <Layers size={14} /> Panel
                </button>
              </div>
            </div>

            {/* Main Workspace Layout */}
            <div className="workspace-container">
              {/* Left/Main Area: High-Density Operation Table */}
              <div className="table-viewport">
                <div className="op-table-container">
                  <table className="op-table">
                    <thead>
                      <tr>
                        <th style={{ width: 34, textAlign: 'center' }}>
                          <input
                            type="checkbox"
                            checked={selectedDeviceIds.size > 0 && selectedDeviceIds.size === filteredAndSortedDevices.length}
                            onChange={toggleSelectAll}
                          />
                        </th>
                        <th className="sortable" onClick={() => handleSort("status")} style={{ width: 100 }}>
                          Durum
                        </th>
                        <th className="sortable" onClick={() => handleSort("deviceName")}>
                          Cihaz Adı {sortField === "deviceName" && (sortDirection === "asc" ? "▲" : "▼")}
                        </th>
                        <th>Aktif Kullanıcı</th>
                        <th>IP / Lokasyon</th>
                        <th className="sortable" onClick={() => handleSort("cpu")}>
                          CPU / RAM {sortField === "cpu" && (sortDirection === "asc" ? "▲" : "▼")}
                        </th>
                        <th className="sortable" onClick={() => handleSort("agentVersion")}>
                          Ajan
                        </th>
                        <th className="sortable" onClick={() => handleSort("lastSeen")} style={{ textAlign: 'right' }}>
                          Son Sinyal {sortField === "lastSeen" && (sortDirection === "asc" ? "▲" : "▼")}
                        </th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredAndSortedDevices.length === 0 ? (
                        <tr>
                          <td colSpan={8} style={{ textAlign: 'center', padding: '32px 0', color: '#94a3b8' }}>
                            Kriterlere uygun cihaz bulunamadı.
                          </td>
                        </tr>
                      ) : (
                        filteredAndSortedDevices.map((d) => {
                          const isSelected = selectedDeviceId === d.id;
                          const isChecked = selectedDeviceIds.has(d.id);
                          const cpuVal = d.cpuUsagePercent || 0;
                          const hasUpdate = Boolean(latestAgentVersion && d.agentVersion !== latestAgentVersion);

                          return (
                            <tr
                              key={d.id}
                              className={`table-row ${isSelected ? "selected" : ""}`}
                              onClick={() => setSelectedDeviceId(d.id)}
                            >
                              <td style={{ textAlign: 'center' }} onClick={(e) => toggleSelectDevice(d.id, e)}>
                                <input
                                  type="checkbox"
                                  checked={isChecked}
                                  onChange={() => {}}
                                />
                              </td>

                              <td>
                                <span className={`status-pill-inline ${d.isOnline ? (hasUpdate ? "warn" : "online") : "offline"}`}>
                                  <span className={`status-dot ${d.isOnline ? (hasUpdate ? "warn" : "online") : "offline"}`} />
                                  {d.isOnline ? (hasUpdate ? "Güncelleme" : "Çevrimiçi") : "Çevrimdışı"}
                                </span>
                              </td>

                              <td>
                                <div style={{ fontWeight: 600, color: '#0f172a' }}>{d.deviceName}</div>
                              </td>

                              <td>
                                <span style={{ color: '#475569' }}>{d.activeUser || "—"}</span>
                              </td>

                              <td>
                                <span className="mono-text">{d.ipAddress}</span>
                                {d.locationCode && <span style={{ color: '#94a3b8', marginLeft: 6 }}>· {d.locationCode}</span>}
                              </td>

                              <td>
                                <div className="mini-gauge-wrapper">
                                  <div className="mini-gauge-bar">
                                    <div
                                      className="mini-gauge-fill"
                                      style={{
                                        width: `${Math.min(100, Math.max(3, cpuVal))}%`,
                                        background: cpuVal > 85 ? '#ef4444' : cpuVal > 60 ? '#f59e0b' : '#2563eb'
                                      }}
                                    />
                                  </div>
                                  <span className="mono-text" style={{ fontSize: 11 }}>%{cpuVal}</span>
                                </div>
                              </td>

                              <td>
                                <span className="mono-text" style={{ color: hasUpdate ? '#b45309' : '#475569', fontWeight: hasUpdate ? 600 : 400 }}>
                                  v{d.agentVersion}
                                </span>
                              </td>

                              <td style={{ textAlign: 'right' }}>
                                <span style={{ color: '#64748b', fontSize: 11.5 }}>
                                  {d.isOnline ? "şimdi" : new Date(d.lastSeenAt).toLocaleTimeString("tr-TR", { hour: '2-digit', minute: '2-digit' })}
                                </span>
                              </td>
                            </tr>
                          );
                        })
                      )}
                    </tbody>
                  </table>
                </div>

                {/* Bulk Actions Floating Bar */}
                {selectedDeviceIds.size > 0 && (
                  <div className="bulk-bar">
                    <span className="bulk-count">{selectedDeviceIds.size} cihaz seçildi</span>
                    <button className="bulk-btn bulk-btn-primary" onClick={handleBulkUpdateAgents}>
                      🚀 Toplu Ajan Güncelle
                    </button>
                    <button className="bulk-btn bulk-btn-secondary" onClick={() => setSelectedDeviceIds(new Set())}>
                      Seçimi Temizle
                    </button>
                  </div>
                )}
              </div>

              {/* Right Side: Compact Detail Drawer for Selected Device */}
              {selectedDevice && (
                <aside className="detail-drawer">
                  <div className="drawer-header">
                    <div className="drawer-title-row">
                      <h2 className="drawer-title">{selectedDevice.deviceName}</h2>
                      <span className={`status-pill-inline ${selectedDevice.isOnline ? "online" : "offline"}`}>
                        <span className={`status-dot ${selectedDevice.isOnline ? "online" : "offline"}`} />
                        {selectedDevice.isOnline ? "Çevrimiçi" : "Çevrimdışı"}
                      </span>
                    </div>

                    <div className="drawer-actions">
                      <button
                        className="btn-primary"
                        onClick={() => handleConnect(selectedDevice.id)}
                        disabled={!selectedDevice.isOnline || connectingId === selectedDevice.id}
                      >
                        <PlugZap size={14} />
                        {connectingId === selectedDevice.id ? "Bağlanıyor..." : "Oturum Başlat"}
                      </button>

                      {latestAgentVersion && selectedDevice.agentVersion !== latestAgentVersion && (
                        <button
                          className="btn-secondary"
                          onClick={() => handleUpdateAgent(selectedDevice.id)}
                          disabled={!selectedDevice.isOnline || updatingDeviceId === selectedDevice.id}
                          title="Agent'ı son sürüme güncelle"
                        >
                          🚀 Güncelle
                        </button>
                      )}
                    </div>
                  </div>

                  {/* Drawer Navigation Tabs */}
                  <div className="drawer-tabs">
                    <button
                      className={`drawer-tab ${activeDetailTab === "specs" ? "active" : ""}`}
                      onClick={() => setActiveDetailTab("specs")}
                    >
                      Donanım
                    </button>
                    <button
                      className={`drawer-tab ${activeDetailTab === "terminal" ? "active" : ""}`}
                      onClick={() => setActiveDetailTab("terminal")}
                    >
                      Terminal
                    </button>
                    <button
                      className={`drawer-tab ${activeDetailTab === "activity" ? "active" : ""}`}
                      onClick={() => setActiveDetailTab("activity")}
                    >
                      Aktivite
                    </button>
                  </div>

                  {/* Drawer Tab Content */}
                  <div className="drawer-body">
                    {activeDetailTab === "specs" && (
                      <>
                        {/* Live Gauges */}
                        <div className="gauge-block">
                          <div className="gauge-label-row">
                            <span>CPU Kullanımı</span>
                            <span className="gauge-val">%{selectedDevice.cpuUsagePercent || 0}</span>
                          </div>
                          <div className="gauge-bar-full">
                            <div
                              className="gauge-bar-val"
                              style={{ width: `${Math.min(100, Math.max(3, selectedDevice.cpuUsagePercent || 0))}%` }}
                            />
                          </div>
                        </div>

                        <div className="gauge-block">
                          <div className="gauge-label-row">
                            <span>RAM (Bellek)</span>
                            <span className="gauge-val">
                              {((selectedDevice.memoryUsedMb || 0) / 1024).toFixed(1)} / {((selectedDevice.memoryTotalMb || 0) / 1024).toFixed(1)} GB
                            </span>
                          </div>
                          <div className="gauge-bar-full">
                            <div
                              className="gauge-bar-val"
                              style={{
                                width: `${Math.min(100, Math.max(3, ((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) * 100))}%`
                              }}
                            />
                          </div>
                        </div>

                        <div className="gauge-block">
                          <div className="gauge-label-row">
                            <span>Boş Disk Alanı (C:)</span>
                            <span className="gauge-val">{((selectedDevice.diskFreeMb || 0) / 1024).toFixed(1)} GB Boş</span>
                          </div>
                        </div>

                        <hr style={{ border: 'none', borderTop: '1px solid var(--border-color)', margin: '4px 0' }} />

                        {/* Specs Key-Value Rows */}
                        <div className="spec-row">
                          <span className="spec-label">İşletim Sistemi</span>
                          <span className="spec-value">{selectedDevice.operatingSystem}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">IP Adresi</span>
                          <span className="spec-value mono-text">{selectedDevice.ipAddress}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">Aktif Kullanıcı</span>
                          <span className="spec-value">{selectedDevice.activeUser || "—"}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">Domain / Çalışma Grubu</span>
                          <span className="spec-value">{selectedDevice.domainName || "WORKGROUP"}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">Lokasyon Kodu</span>
                          <span className="spec-value">{selectedDevice.locationCode || "OFFICE"}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">Ajan Sürümü</span>
                          <span className="spec-value mono-text">v{selectedDevice.agentVersion}</span>
                        </div>

                        <div className="spec-row">
                          <span className="spec-label">Cihaz ID</span>
                          <span className="spec-value mono-text" style={{ fontSize: 11 }}>
                            {selectedDevice.id.slice(0, 8)}...
                            <button
                              style={{ marginLeft: 6, background: 'none', border: 'none', cursor: 'pointer', color: '#64748b' }}
                              onClick={() => copyToClipboard(selectedDevice.id, "Cihaz ID")}
                            >
                              <Copy size={11} />
                            </button>
                          </span>
                        </div>
                      </>
                    )}

                    {activeDetailTab === "terminal" && (
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
                        <div style={{ background: '#0f172a', color: '#38bdf8', padding: 10, borderRadius: 8, fontFamily: 'monospace', fontSize: 11.5, height: 180, overflowY: 'auto' }}>
                          <div>Microsoft Windows [{selectedDevice.operatingSystem}]</div>
                          <div>NexMote Agent v{selectedDevice.agentVersion} - Komut Hattı Hazır.</div>
                          <div style={{ marginTop: 8, color: '#94a3b8' }}>Komut göndermek için Teknisyen masaüstü konsolunu kullanabilir veya API üzerinden tetikleyebilirsiniz.</div>
                        </div>
                        <button
                          className="btn-primary"
                          onClick={() => handleConnect(selectedDevice.id)}
                        >
                          <Terminal size={14} /> Masaüstü Konsolunda Aç
                        </button>
                      </div>
                    )}

                    {activeDetailTab === "activity" && (
                      <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
                        {activityLogs.length === 0 ? (
                          <div style={{ color: '#94a3b8', fontSize: 12, textAlign: 'center', padding: '16px 0' }}>
                            Henüz kayıtlı işlem yok.
                          </div>
                        ) : (
                          activityLogs.map((log) => (
                            <div key={log.id} style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 8, fontSize: 12 }}>
                              <span style={{ color: '#334155' }}>{log.text}</span>
                              <span style={{ color: '#94a3b8', fontSize: 11, fontFamily: 'monospace' }}>{log.time}</span>
                            </div>
                          ))
                        )}
                      </div>
                    )}
                  </div>
                </aside>
              )}
            </div>
          </>
        )}

        {/* View 2: Downloads Package Catalog */}
        {view === "downloads" && (
          <div className="content-pane">
            <div className="content-card">
              <h2 className="content-card-title">Kurulum Paketleri Kataloğu (MSI Dağıtımı)</h2>
              <p style={{ color: '#64748b', fontSize: 13, marginBottom: 16 }}>
                Active Directory GPO, SCCM, Intune veya doğrudan elle kurulum için hazır Windows MSI paketleri.
              </p>

              <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
                {downloads.map((pkg) => (
                  <div
                    key={pkg.fileName}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '14px 16px',
                      border: '1px solid var(--border-color)',
                      borderRadius: 'var(--radius-control)',
                      background: '#fff'
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
                      <div style={{ width: 36, height: 36, borderRadius: 8, background: '#eff6ff', display: 'flex', alignItems: 'center', justifyContent: 'center', color: '#2563eb' }}>
                        <Download size={18} />
                      </div>
                      <div>
                        <div style={{ fontWeight: 600, color: '#0f172a' }}>{pkg.name}</div>
                        <div className="mono-text" style={{ fontSize: 11 }}>
                          {pkg.fileName} · {(pkg.sizeBytes / (1024 * 1024)).toFixed(1)} MB · {pkg.description}
                        </div>
                      </div>
                    </div>

                    <a
                      href={pkg.url}
                      download
                      className="btn-secondary"
                      style={{ textDecoration: 'none' }}
                    >
                      <Download size={14} /> İndir
                    </a>
                  </div>
                ))}
              </div>
            </div>
          </div>
        )}

        {/* View 3: Server Settings */}
        {view === "settings" && (
          <div className="content-pane">
            <div className="content-card">
              <h2 className="content-card-title">Sunucu & Kayıt Yapılandırması</h2>
              <p style={{ color: '#64748b', fontSize: 13, marginBottom: 16 }}>
                Agent cihazlarının sunucuya kayıt olması ve heartbeat sinyalleşme parametreleri.
              </p>

              <form onSubmit={handleSaveSettings} style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
                <div className="form-group">
                  <label className="form-label">Sunucu Bağlantı Adresi (Server URL)</label>
                  <input
                    type="url"
                    className="form-input"
                    value={settings.serverUrl}
                    onChange={(e) => setSettings({ ...settings, serverUrl: e.target.value })}
                    required
                  />
                  <span style={{ fontSize: 11.5, color: '#94a3b8' }}>Agent ve Teknisyen uygulamalarının bağlandığı güvenli alan adı.</span>
                </div>

                <div className="form-group">
                  <label className="form-label">Kayıt Doğrulama Anahtarı (Enrollment Key)</label>
                  <input
                    type="password"
                    className="form-input"
                    value={settings.enrollmentKey}
                    onChange={(e) => setSettings({ ...settings, enrollmentKey: e.target.value })}
                    required
                  />
                  <span style={{ fontSize: 11.5, color: '#94a3b8' }}>Yeni istemci kurulumlarında kullanılan yetkilendirme anahtarı.</span>
                </div>

                <div className="form-group">
                  <label className="form-label">Heartbeat Sinyal Sıklığı (Saniye)</label>
                  <input
                    type="number"
                    min={5}
                    max={300}
                    className="form-input"
                    value={settings.heartbeatSeconds}
                    onChange={(e) => setSettings({ ...settings, heartbeatSeconds: Number(e.target.value) })}
                    required
                  />
                </div>

                <div className="form-group">
                  <label className="form-label">Varsayılan Lokasyon Kodu</label>
                  <input
                    type="text"
                    className="form-input"
                    value={settings.defaultLocationCode}
                    onChange={(e) => setSettings({ ...settings, defaultLocationCode: e.target.value })}
                  />
                </div>

                <button
                  type="submit"
                  className="btn-primary"
                  style={{ height: 38, width: 180, marginTop: 8 }}
                  disabled={savingSettings}
                >
                  <Save size={14} />
                  {savingSettings ? "Kaydediliyor..." : "Ayarları Kaydet"}
                </button>
              </form>
            </div>
          </div>
        )}
      </div>

      {/* Floating Notification Toast */}
      {status && (
        <div className="toast-container">
          {status}
        </div>
      )}
    </div>
  );
}
