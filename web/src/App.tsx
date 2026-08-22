import {
  Activity,
  AlertCircle,
  ArrowLeft,
  ArrowUpDown,
  Bell,
  Check,
  CheckCircle2,
  ChevronDown,
  ChevronUp,
  Clock,
  Copy,
  Cpu,
  Download,
  Eye,
  EyeOff,
  Globe,
  HardDrive,
  Laptop,
  LayoutDashboard,
  Lock,
  LogOut,
  Monitor,
  Package,
  Play,
  PlugZap,
  Radio,
  RefreshCw,
  Save,
  Search,
  Send,
  Server,
  Settings,
  Shield,
  ShieldCheck,
  Sliders,
  Sparkles,
  Terminal,
  Trash2,
  User,
  Wifi,
  X,
  Zap,
  Database,
  ExternalLink
} from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import {
  checkUpdates,
  clearStoredAdminToken,
  createRemoteSession,
  deleteDevice,
  DeviceSummary,
  DownloadPackage,
  downloadSilentInstaller,
  executeDeviceCommand,
  getServerMetrics,
  getServerSettings,
  getStoredAdminToken,
  InstalledAppInfo,
  listDevices,
  listDownloads,
  login,
  ServerMetrics,
  ServerSettings,
  setStoredAdminToken,
  triggerAgentUpdate,
  uninstallApp,
  updateServerSettings,
  WindowsUpdateInfo
} from "./api";

type View = "devices" | "device-detail" | "downloads" | "settings";
type StatusFilter = "all" | "online" | "offline" | "warning";
type DetailTab = "overview" | "specs" | "performance" | "network" | "applications" | "updates" | "terminal" | "activity";
type SortField = "deviceName" | "status" | "activeUser" | "ipAddress" | "cpu" | "agentVersion" | "lastSeen";
type SortDirection = "asc" | "desc";

function isVersionOlder(installed?: string | null, latest?: string | null): boolean {
  if (!installed || !latest) return false;
  const a = installed.split(".").map(Number);
  const b = latest.split(".").map(Number);
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const x = a[i] || 0;
    const y = b[i] || 0;
    if (x !== y) return x < y;
  }
  return false;
}

function renderSortIndicator(field: SortField, currentField: SortField, direction: SortDirection) {
  const isActive = currentField === field;
  return (
    <span className={`sort-indicator ${isActive ? "active" : "idle"}`}>
      {isActive ? (
        direction === "asc" ? (
          <ChevronUp size={13} className="sort-chevron asc" />
        ) : (
          <ChevronDown size={13} className="sort-chevron desc" />
        )
      ) : (
        <ArrowUpDown size={11} className="sort-chevron idle" />
      )}
    </span>
  );
}

/**
 * Bir cihazın son görülme zamanını "12 dk önce" gibi göreli, kısa bir metne çevirir.
 * Çevrimdışı cihazlarda gösterilen değerlerin ne kadar eski (stale) olduğunu belirginleştirmek için kullanılır.
 */
function formatLastSeen(lastSeenAt: string): string {
  const diffMs = Date.now() - new Date(lastSeenAt).getTime();
  const diffMinutes = Math.floor(diffMs / 60000);

  if (diffMinutes < 1) return "az önce";
  if (diffMinutes < 60) return `${diffMinutes} dk önce`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} sa önce`;

  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays} gün önce`;
}

function cleanUserName(rawUser?: string): string {
  if (!rawUser) return "—";
  let user = rawUser.trim();
  const backslashIdx = user.lastIndexOf("\\");
  if (backslashIdx >= 0 && backslashIdx < user.length - 1) {
    user = user.substring(backslashIdx + 1);
  }
  const atIdx = user.indexOf("@");
  if (atIdx > 0) {
    user = user.substring(0, atIdx);
  }
  user = user.trim();
  if (user.endsWith("$") || user.toLowerCase() === "system") return "—";
  return user || "—";
}

function formatUptime(seconds: number): string {
  if (seconds < 60) return `${seconds} sn`;
  const m = Math.floor(seconds / 60);
  if (m < 60) return `${m} dk`;
  const h = Math.floor(m / 60);
  const remM = m % 60;
  if (h < 24) return `${h} sa ${remM} dk`;
  const d = Math.floor(h / 24);
  const remH = h % 24;
  return `${d} gün ${remH} sa`;
}

function formatOsName(rawOs?: string): string {
  if (!rawOs) return "Windows";
  const str = rawOs.trim();
  
  if (str.startsWith("Windows 11") || str.startsWith("Windows 10") || str.startsWith("Windows Server")) {
    return str;
  }

  const ntMatch = str.match(/10\.0\.(\d+)/);
  if (ntMatch) {
    const build = parseInt(ntMatch[1], 10);
    if (build >= 26100) return `Windows 11 Pro (24H2) [${build}]`;
    if (build >= 22631) return `Windows 11 Pro (23H2) [${build}]`;
    if (build >= 22621) return `Windows 11 Pro (22H2) [${build}]`;
    if (build >= 22000) return `Windows 11 Pro (21H2) [${build}]`;
    if (build >= 19045) return `Windows 10 Pro (22H2) [${build}]`;
    if (build >= 19044) return `Windows 10 Pro (21H2) [${build}]`;
    if (build >= 19043) return `Windows 10 Pro (21H1) [${build}]`;
    if (build >= 19042) return `Windows 10 Pro (20H2) [${build}]`;
    if (build >= 19041) return `Windows 10 Pro (2004) [${build}]`;
    if (build >= 17763) return `Windows 10 Pro (1809) [${build}]`;
    if (build >= 14393) return `Windows 10 Pro (1607) [${build}]`;
    if (build >= 10240) return `Windows 10 [${build}]`;
  }

  if (str.includes("6.3")) return "Windows 8.1";
  if (str.includes("6.1")) return "Windows 7";

  return str.replace("Microsoft Windows NT", "Windows NT");
}

function renderSparkline(data: number[], color: string, maxVal: number) {
  if (data.length < 2) {
    return <div className="sparkline-placeholder">Canlı veri toplanıyor...</div>;
  }
  const width = 320;
  const height = 46;
  const points = data.map((val, idx) => {
    const x = (idx / (data.length - 1)) * width;
    const norm = maxVal > 0 ? Math.min(val, maxVal) / maxVal : 0;
    const y = height - norm * (height - 10) - 5;
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  }).join(" ");

  return (
    <svg width="100%" height={height} viewBox={`0 0 ${width} ${height}`} className="sparkline-svg" preserveAspectRatio="none">
      <polyline
        fill="none"
        stroke={color}
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        points={points}
      />
    </svg>
  );
}

export function App() {
  const [devices, setDevices] = useState<DeviceSummary[]>([]);
  const [downloads, setDownloads] = useState<DownloadPackage[]>([]);
  const [updatingDeviceId, setUpdatingDeviceId] = useState<string | null>(null);
  const [silentInstallerDownloading, setSilentInstallerDownloading] = useState(false);
  const [settings, setSettings] = useState<ServerSettings>({
    serverUrl: "https://nexmote.com",
    enrollmentKey: "dev-enrollment-key",
    heartbeatSeconds: 20,
    defaultLocationCode: "OFFICE"
  });

  // Server Performance Metrics State
  const [serverMetrics, setServerMetrics] = useState<ServerMetrics | null>(null);
  const [metricsLoading, setMetricsLoading] = useState(false);
  const [cpuHistory, setCpuHistory] = useState<number[]>([]);
  const [netHistory, setNetHistory] = useState<number[]>([]);

  const [view, setView] = useState<View>("devices");
  const [selectedDeviceId, setSelectedDeviceId] = useState<string | null>(null);
  const [selectedDeviceIds, setSelectedDeviceIds] = useState<Set<string>>(new Set());
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [sortField, setSortField] = useState<SortField>("deviceName");
  const [sortDirection, setSortDirection] = useState<SortDirection>("asc");
  const [status, setStatus] = useState<string | null>(null);
  const [showNotifications, setShowNotifications] = useState(false);
  const [loading, setLoading] = useState(false);
  const [savingSettings, setSavingSettings] = useState(false);
  const [connectingId, setConnectingId] = useState<string | null>(null);
  const [latestAgentVersion, setLatestAgentVersion] = useState<string | null>(null);
  const [activeDetailTab, setActiveDetailTab] = useState<DetailTab>("overview");
  const [copiedField, setCopiedField] = useState<string | null>(null);
  const [appSearchQuery, setAppSearchQuery] = useState("");
  const [updateSearchQuery, setUpdateSearchQuery] = useState("");

  // Authentication State
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(() => Boolean(getStoredAdminToken()));
  const [loginEmail, setLoginEmail] = useState("");
  const [loginPassword, setLoginPassword] = useState("");
  const [showLoginPassword, setShowLoginPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [authError, setAuthError] = useState("");
  const [isLoggingIn, setIsLoggingIn] = useState(false);

  // Live Activity Event Logs
  const [activityLogs, setActivityLogs] = useState<{ id: string; text: string; time: string; level: "info" | "success" | "warn" }[]>([]);

  // Delete Device Modal State
  const [deleteModal, setDeleteModal] = useState<{
    isOpen: boolean;
    deviceIds: string[];
    deviceNames: string[];
    isOnline: boolean;
    uninstallAgent: boolean;
  } | null>(null);

  // Web Terminal Interactive State
  type TerminalShell = "cmd" | "powershell";
  const [terminalShell, setTerminalShell] = useState<TerminalShell>("cmd");
  const [terminalInput, setTerminalInput] = useState<string>("");
  const [terminalRunning, setTerminalRunning] = useState<boolean>(false);
  const [terminalLogs, setTerminalLogs] = useState<{
    id: string;
    shell: string;
    command: string;
    time: string;
    exitCode: number;
    stdOut: string;
    stdErr: string;
    durationMs: number;
    timedOut: boolean;
    elevationDenied: boolean;
  }[]>([]);
  const [cmdHistory, setCmdHistory] = useState<string[]>([]);
  const [historyIndex, setHistoryIndex] = useState<number>(-1);
  const terminalBottomRef = useRef<HTMLDivElement>(null);

  // Silent App Uninstall State
  const [uninstallingApp, setUninstallingApp] = useState<InstalledAppInfo | null>(null);
  const [isUninstalling, setIsUninstalling] = useState<boolean>(false);
  const [uninstallResult, setUninstallResult] = useState<{
    appName: string;
    success: boolean;
    message: string;
    stdOut?: string;
    stdErr?: string;
  } | null>(null);

  async function handleUninstallApp(app: InstalledAppInfo) {
    if (!selectedDevice) return;
    try {
      setIsUninstalling(true);
      setUninstallResult(null);
      const res = await uninstallApp(selectedDevice.id, {
        appName: app.name,
        uninstallString: app.uninstallString,
        quietUninstallString: app.quietUninstallString
      });
      setUninstallResult({
        appName: app.name,
        success: res.success,
        message: res.message,
        stdOut: res.stdOut,
        stdErr: res.stdErr
      });
      if (res.success) {
        setDevices(prev => prev.map(d => {
          if (d.id === selectedDevice.id && d.installedApps) {
            return {
              ...d,
              installedApps: d.installedApps.filter(a => a.name.toLowerCase() !== app.name.toLowerCase())
            };
          }
          return d;
        }));
        setStatus(`${app.name} başarıyla sessizce kaldırıldı.`);
        setTimeout(() => {
          refresh(false);
        }, 1500);
      } else {
        setStatus(`${app.name} kaldırma tamamlandı (Kod: ${res.exitCode})`);
      }
    } catch (err: any) {
      setUninstallResult({
        appName: app.name,
        success: false,
        message: err.message || "Kaldırma işlemi sırasında bir hata oluştu."
      });
      setStatus(`Kaldırma hatası: ${err.message}`);
    } finally {
      setIsUninstalling(false);
    }
  }

  async function handleRunTerminalCommand(customCmd?: string) {
    const cmdToRun = (customCmd ?? terminalInput).trim();
    if (!cmdToRun || !selectedDevice || terminalRunning) return;

    if (!selectedDevice.isOnline) {
      alert("Cihaz çevrimdışı olduğundan komut gönderilemez.");
      return;
    }

    setTerminalRunning(true);
    setTerminalInput("");
    setCmdHistory(prev => [cmdToRun, ...prev.filter(c => c !== cmdToRun)].slice(0, 50));
    setHistoryIndex(-1);

    const now = new Date().toLocaleTimeString("tr-TR");

    try {
      const res = await executeDeviceCommand(
        selectedDevice.id,
        terminalShell,
        cmdToRun,
        false,
        45
      );

      setTerminalLogs(prev => [
        ...prev,
        {
          id: res.requestId || Math.random().toString(),
          shell: res.shell || terminalShell,
          command: cmdToRun,
          time: now,
          exitCode: res.exitCode,
          stdOut: res.stdOut,
          stdErr: res.stdErr,
          durationMs: res.durationMs,
          timedOut: res.timedOut,
          elevationDenied: res.elevationDenied
        }
      ]);
    } catch (err: any) {
      setTerminalLogs(prev => [
        ...prev,
        {
          id: Math.random().toString(),
          shell: terminalShell,
          command: cmdToRun,
          time: now,
          exitCode: -1,
          stdOut: "",
          stdErr: err?.message || "Komut çalıştırılırken bir hata oluştu.",
          durationMs: 0,
          timedOut: false,
          elevationDenied: false
        }
      ]);
    } finally {
      setTerminalRunning(false);
      setTimeout(() => {
        terminalBottomRef.current?.scrollIntoView({ behavior: "smooth" });
      }, 50);
    }
  }

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
      setAuthError("E-posta veya parola hatalı.");
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

  async function refreshServerMetrics(isManual: boolean = false) {
    if (isManual) setMetricsLoading(true);
    try {
      const data = await getServerMetrics();
      setServerMetrics(data);
      setCpuHistory(prev => [...prev.slice(-19), data.cpuUsagePercent]);
      const totalNetMbps = data.networkInMbps + data.networkOutMbps;
      setNetHistory(prev => [...prev.slice(-19), totalNetMbps]);
      if (isManual) showToast("Sunucu metrikleri güncellendi.");
    } catch {
      // sessizce geç
    } finally {
      if (isManual) setMetricsLoading(false);
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
    refreshServerMetrics();

    const interval = setInterval(() => {
      refresh(false);
      if (view === "settings") {
        refreshServerMetrics(false);
      }
    }, 3000);
    return () => clearInterval(interval);
  }, [isAuthenticated, view]);

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
      showToast("Ajan güncelleme emri iletildi.");
      addActivityLog(`Ajan güncelleme emri iletildi (${deviceId.slice(0, 8)})`, "success");
      await refresh();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Güncelleme tetiklenemedi");
      addActivityLog(`Ajan güncelleme başarısız: ${error instanceof Error ? error.message : "Hata"}`, "warn");
    } finally {
      setUpdatingDeviceId(null);
    }
  }

  async function handleDownloadSilentInstaller() {
    setSilentInstallerDownloading(true);
    try {
      await downloadSilentInstaller();
      addActivityLog("Sessiz kurulum paketi indirildi.", "success");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Sessiz kurulum paketi indirilemedi");
      addActivityLog(`Sessiz kurulum paketi indirilemedi: ${error instanceof Error ? error.message : "Hata"}`, "warn");
    } finally {
      setSilentInstallerDownloading(false);
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

  function handleDeleteDevice(deviceId: string, deviceName?: string, isOnline = true) {
    const name = deviceName || "Seçilen cihaz";
    setDeleteModal({
      isOpen: true,
      deviceIds: [deviceId],
      deviceNames: [name],
      isOnline,
      uninstallAgent: isOnline
    });
  }

  function handleBulkDeleteDevices() {
    if (selectedDeviceIds.size === 0) return;
    const names = Array.from(selectedDeviceIds).map(id => {
      const dev = devices.find(d => d.id === id);
      return dev ? dev.deviceName : id;
    });
    setDeleteModal({
      isOpen: true,
      deviceIds: Array.from(selectedDeviceIds),
      deviceNames: names,
      isOnline: true,
      uninstallAgent: true
    });
  }

  async function confirmDeleteDevices() {
    if (!deleteModal) return;
    const { deviceIds, deviceNames, uninstallAgent } = deleteModal;
    setDeleteModal(null);

    const isSingle = deviceIds.length === 1;
    const displayName = isSingle ? deviceNames[0] : `${deviceIds.length} cihaz`;

    showToast(uninstallAgent ? `${displayName} siliniyor ve ajan kaldırılıyor...` : `${displayName} siliniyor...`);

    for (const id of deviceIds) {
      try {
        await deleteDevice(id, uninstallAgent);
      } catch (err: any) {
        showToast(err?.message || "Cihaz silinemedi.");
      }
    }

    if (uninstallAgent) {
      showToast(`${displayName} kaydı silindi ve hedef bilgisayardan ajan başarıyla kaldırıldı.`);
      addActivityLog(`${displayName} silindi ve ajanı kaldırıldı`, "warn");
    } else {
      showToast(`${displayName} kaydı başarıyla silindi.`);
      addActivityLog(`${displayName} silindi`, "warn");
    }

    if (selectedDeviceId && deviceIds.includes(selectedDeviceId)) {
      setSelectedDeviceId(null);
      setView("devices");
    }

    setSelectedDeviceIds(prev => {
      const next = new Set(prev);
      deviceIds.forEach(id => next.delete(id));
      return next;
    });

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
        isVersionOlder(d.agentVersion, latestAgentVersion) ||
        (d.cpuUsagePercent && d.cpuUsagePercent > 90)
      );

      if (statusFilter === "online") return matchesQuery && d.isOnline;
      if (statusFilter === "offline") return matchesQuery && !d.isOnline;
      if (statusFilter === "warning") return matchesQuery && isWarning;
      return matchesQuery;
    });

    result.sort((a, b) => {
      let cmp = 0;
      if (sortField === "deviceName") {
        cmp = (a.deviceName || "").localeCompare(b.deviceName || "", undefined, { sensitivity: "base" });
      } else if (sortField === "status") {
        cmp = Number(b.isOnline) - Number(a.isOnline);
      } else if (sortField === "activeUser") {
        cmp = cleanUserName(a.activeUser).localeCompare(cleanUserName(b.activeUser), undefined, { sensitivity: "base" });
      } else if (sortField === "ipAddress") {
        cmp = (a.ipAddress || "").localeCompare(b.ipAddress || "", undefined, { numeric: true });
      } else if (sortField === "cpu") {
        cmp = (a.cpuUsagePercent || 0) - (b.cpuUsagePercent || 0);
      } else if (sortField === "agentVersion") {
        cmp = (a.agentVersion || "").localeCompare(b.agentVersion || "", undefined, { numeric: true });
      } else if (sortField === "lastSeen") {
        const tA = a.lastSeenAt ? new Date(a.lastSeenAt).getTime() : 0;
        const tB = b.lastSeenAt ? new Date(b.lastSeenAt).getTime() : 0;
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

  const filteredApps = useMemo(() => {
    if (!selectedDevice?.installedApps) return [];
    const q = appSearchQuery.trim().toLowerCase();
    if (!q) return selectedDevice.installedApps;
    return selectedDevice.installedApps.filter(
      app =>
        app.name.toLowerCase().includes(q) ||
        (app.publisher && app.publisher.toLowerCase().includes(q)) ||
        (app.version && app.version.toLowerCase().includes(q))
    );
  }, [selectedDevice?.installedApps, appSearchQuery]);

  const filteredUpdates = useMemo(() => {
    if (!selectedDevice?.windowsUpdates) return [];
    const q = updateSearchQuery.trim().toLowerCase();
    if (!q) return selectedDevice.windowsUpdates;
    return selectedDevice.windowsUpdates.filter(
      u =>
        u.hotFixId.toLowerCase().includes(q) ||
        (u.description && u.description.toLowerCase().includes(q)) ||
        (u.installedOn && u.installedOn.toLowerCase().includes(q)) ||
        (u.installedBy && u.installedBy.toLowerCase().includes(q)) ||
        (u.status && u.status.toLowerCase().includes(q))
    );
  }, [selectedDevice?.windowsUpdates, updateSearchQuery]);

  const userInitial = (loginEmail || "N").charAt(0).toUpperCase();
  const userDisplayName = loginEmail ? loginEmail.split("@")[0] : "Yönetici";
  const onlineCount = devices.filter((d) => d.isOnline).length;
  const warningCount = devices.filter((d) => isVersionOlder(d.agentVersion, latestAgentVersion)).length;

  function toggleSelectAll() {
    if (selectedDeviceIds.size === filteredAndSortedDevices.length) {
      setSelectedDeviceIds(new Set());
    } else {
      setSelectedDeviceIds(new Set(filteredAndSortedDevices.map(d => d.id)));
    }
  }

  function toggleSelectDevice(id: string) {
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
            <div className="login-brand-mark">
              <ShieldCheck size={20} color="#fff" />
            </div>
            <span>NexMote</span>
          </div>

          <div className="login-trust-info">
            <h2 className="login-trust-heading">Kendi sunucunuzda çalışan kurumsal uzaktan yönetim.</h2>
            <div className="login-trust-item">
              <div className="status-dot online" />
              <span className="login-trust-domain">{settings.serverUrl.replace(/^https?:\/\//, '')}</span>
            </div>
            <div className="login-trust-item">
              <Shield size={14} color="#94a3b8" />
              <span>TLS 1.3 · Güvenli oturum trafiği</span>
            </div>
            <div className="login-trust-item">
              <Server size={14} color="#94a3b8" />
              <span>Canlı sinyalleşme hazır</span>
            </div>
          </div>

          <div className="login-footnote">
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

            <form onSubmit={handleLogin} className="login-form">
              {authError && (
                <div className="login-error-text">
                  {authError}
                </div>
              )}

              <div className="form-group">
                <label className="form-label">E-posta adresi</label>
                <div className="form-input-wrapper">
                  <input
                    type="email"
                    className="form-input"
                    placeholder="ornek@nexmote.com"
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
                    placeholder="Parolanız"
                    value={loginPassword}
                    onChange={(e) => setLoginPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    className="password-toggle-btn"
                    onClick={() => setShowLoginPassword(!showLoginPassword)}
                    title={showLoginPassword ? "Gizle" : "Göster"}
                    aria-label={showLoginPassword ? "Parolayı gizle" : "Parolayı göster"}
                  >
                    {showLoginPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </div>

              <div className="login-options-row">
                <label className="remember-label">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                  />
                  Bu cihazda oturumu açık tut
                </label>
              </div>

              <button
                type="submit"
                className="btn-primary"
                data-size="lg"
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

            <div className="user-profile-badge" title={`${loginEmail || userDisplayName} (Yönetici)`}>
              <div className="user-avatar-mini">{userInitial}</div>
              <span className="user-name">{userDisplayName}</span>
              <button
                className="user-logout-mini-btn"
                onClick={handleLogout}
                title="Oturumu Kapat"
              >
                <LogOut size={13} />
              </button>
            </div>
          </div>

          {showNotifications && (
            <aside className="activity-popover" aria-label="Aktivite günlüğü">
              <div className="activity-popover-header">
                <span>Aktivite günlüğü</span>
                <button type="button" onClick={() => setActivityLogs([])} disabled={activityLogs.length === 0}>
                  Temizle
                </button>
              </div>
              <div className="activity-popover-body">
                {activityLogs.length === 0 ? (
                  <div className="activity-empty">Henüz kayıtlı işlem yok.</div>
                ) : (
                  activityLogs.slice(0, 8).map((log) => (
                    <div key={log.id} className={`activity-item ${log.level}`}>
                      <span>{log.text}</span>
                      <time>{log.time}</time>
                    </div>
                  ))
                )}
              </div>
            </aside>
          )}
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
            </div>

            {/* Main Workspace Layout */}
            <div className="workspace-container">
              {/* Left/Main Area: High-Density Operation Table */}
              <div className="table-viewport">
                <div className="op-table-container">
                  <table className="op-table" aria-label="Cihaz envanteri">
                    <thead>
                      <tr>
                        <th className="table-select-col">
                          <input
                            aria-label="Tüm cihazları seç"
                            type="checkbox"
                            checked={selectedDeviceIds.size > 0 && selectedDeviceIds.size === filteredAndSortedDevices.length}
                            onChange={toggleSelectAll}
                          />
                        </th>
                        <th className="sortable table-status-col" onClick={() => handleSort("status")} title="Duruma göre sırala">
                          <div className="th-sort-wrapper">
                            <span>Durum</span>
                            {renderSortIndicator("status", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable" onClick={() => handleSort("deviceName")} title="Cihaz adına göre sırala">
                          <div className="th-sort-wrapper">
                            <span>Cihaz Adı</span>
                            {renderSortIndicator("deviceName", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable" onClick={() => handleSort("activeUser")} title="Aktif kullanıcıya göre sırala">
                          <div className="th-sort-wrapper">
                            <span>Aktif Kullanıcı</span>
                            {renderSortIndicator("activeUser", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable" onClick={() => handleSort("ipAddress")} title="IP adresine göre sırala">
                          <div className="th-sort-wrapper">
                            <span>IP / Lokasyon</span>
                            {renderSortIndicator("ipAddress", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable" onClick={() => handleSort("cpu")} title="CPU kullanımına göre sırala">
                          <div className="th-sort-wrapper">
                            <span>CPU / RAM</span>
                            {renderSortIndicator("cpu", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable" onClick={() => handleSort("agentVersion")} title="Ajan sürümüne göre sırala">
                          <div className="th-sort-wrapper">
                            <span>Ajan</span>
                            {renderSortIndicator("agentVersion", sortField, sortDirection)}
                          </div>
                        </th>
                        <th className="sortable table-time-col" onClick={() => handleSort("lastSeen")} title="Son sinyal zamanına göre sırala">
                          <div className="th-sort-wrapper" style={{ justifyContent: "flex-end", width: "100%" }}>
                            <span>Son Sinyal</span>
                            {renderSortIndicator("lastSeen", sortField, sortDirection)}
                          </div>
                        </th>
                        <th style={{ width: "44px", textAlign: "center" }}>Sil</th>
                      </tr>
                    </thead>
                    <tbody>
                      {filteredAndSortedDevices.length === 0 ? (
                        <tr>
                          <td colSpan={9} className="empty-table-cell">
                            Kriterlere uygun cihaz bulunamadı.
                          </td>
                        </tr>
                      ) : (
                        filteredAndSortedDevices.map((d) => {
                          const isSelected = selectedDeviceId === d.id;
                          const isChecked = selectedDeviceIds.has(d.id);
                          const cpuVal = d.cpuUsagePercent || 0;
                          const hasUpdate = isVersionOlder(d.agentVersion, latestAgentVersion);

                          return (
                            <tr
                              key={d.id}
                              className={`table-row ${isSelected ? "selected" : ""}`}
                              onClick={() => {
                                setSelectedDeviceId(d.id);
                                setView("device-detail");
                              }}
                              tabIndex={0}
                              onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                  e.preventDefault();
                                  setSelectedDeviceId(d.id);
                                  setView("device-detail");
                                }
                              }}
                            >
                              <td className="table-select-col">
                                <input
                                  aria-label={`${d.deviceName} cihazını seç`}
                                  type="checkbox"
                                  checked={isChecked}
                                  onClick={(e) => e.stopPropagation()}
                                  onChange={() => toggleSelectDevice(d.id)}
                                />
                              </td>

                              <td>
                                <span className={`status-pill-inline ${d.isOnline ? (hasUpdate ? "warn" : "online") : "offline"}`}>
                                  <span className={`status-dot ${d.isOnline ? (hasUpdate ? "warn" : "online") : "offline"}`} />
                                  {d.isOnline ? (hasUpdate ? "Güncelleme" : "Çevrimiçi") : "Çevrimdışı"}
                                </span>
                              </td>

                              <td>
                                <div className="device-name-cell">{d.deviceName}</div>
                              </td>

                              <td>
                                <span className="muted-cell">{cleanUserName(d.activeUser)}</span>
                              </td>

                              <td>
                                <span className="mono-text">{d.ipAddress}</span>
                                {d.locationCode && <span className="location-suffix">· {d.locationCode}</span>}
                              </td>

                              <td>
                                <div className="mini-gauge-wrapper">
                                  <div className="mini-gauge-bar">
                                    <div
                                      className="mini-gauge-fill"
                                      style={{
                                        width: `${Math.min(100, Math.max(3, cpuVal))}%`
                                      }}
                                      data-tone={cpuVal > 85 ? "danger" : cpuVal > 60 ? "warn" : "primary"}
                                    />
                                  </div>
                                  <span className="mono-text mono-xs">%{cpuVal}</span>
                                </div>
                              </td>

                              <td>
                                <span className={`mono-text version-cell ${hasUpdate ? "warn" : ""}`}>
                                  v{d.agentVersion}
                                </span>
                              </td>

                              <td className="table-time-col">
                                <span className="dim-time">
                                  {d.isOnline ? "şimdi" : new Date(d.lastSeenAt).toLocaleTimeString("tr-TR", { hour: '2-digit', minute: '2-digit' })}
                                </span>
                              </td>

                              <td className="table-actions-col" onClick={(e) => e.stopPropagation()}>
                                <button
                                  className="icon-del-btn"
                                  onClick={() => handleDeleteDevice(d.id, d.deviceName)}
                                  title={`${d.deviceName} cihazını sil`}
                                  aria-label={`${d.deviceName} cihazını sil`}
                                >
                                  <Trash2 size={13} />
                                </button>
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
                      Toplu ajan güncelle
                    </button>
                    <button className="bulk-btn bulk-btn-danger" onClick={handleBulkDeleteDevices}>
                      <Trash2 size={13} /> Seçilenleri Sil
                    </button>
                    <button className="bulk-btn bulk-btn-secondary" onClick={() => setSelectedDeviceIds(new Set())}>
                      Seçimi Temizle
                    </button>
                  </div>
                )}
              </div>
            </div>
          </>
        )}

        {/* View: Dedicated Full-Page Device Detail View */}
        {view === "device-detail" && selectedDevice && (
          <div className="device-detail-page">
            {/* Consolidated Enterprise Device Command Bar */}
            <div className="detail-command-bar">
              <div className="detail-command-main-row">
                <div className="detail-command-identity">
                  <button
                    className="detail-back-btn"
                    onClick={() => setView("devices")}
                    title="Cihazlar Listesine Dön"
                  >
                    <ArrowLeft size={14} />
                    <span>Cihazlar</span>
                  </button>

                  <div className="detail-command-divider" />

                  <div className="detail-device-title-box">
                    <div className={`detail-device-avatar-mini ${selectedDevice.isOnline ? "online" : "offline"}`}>
                      <Monitor size={15} />
                      <span className={`detail-avatar-pulse-mini ${selectedDevice.isOnline ? "online" : "offline"}`} />
                    </div>
                    <h1 className="detail-device-title">{selectedDevice.deviceName}</h1>
                    <button
                      className="copy-chip-btn"
                      onClick={() => copyToClipboard(selectedDevice.deviceName, "Cihaz Adı")}
                      title="Cihaz adını kopyala"
                    >
                      {copiedField === "Cihaz Adı" ? <Check size={12} /> : <Copy size={12} />}
                    </button>
                  </div>

                  <span className={`status-badge-hero ${selectedDevice.isOnline ? "online" : "offline"}`}>
                    <span className={`status-dot-hero ${selectedDevice.isOnline ? "online" : "offline"}`} />
                    {selectedDevice.isOnline ? "Çevrimiçi" : `Çevrimdışı (${formatLastSeen(selectedDevice.lastSeenAt)})`}
                  </span>
                </div>

                <div className="detail-command-right">
                  <button
                    className="btn-hero-connect"
                    onClick={() => handleConnect(selectedDevice.id)}
                    disabled={!selectedDevice.isOnline || connectingId === selectedDevice.id}
                    title="Teknisyen masaüstü uygulaması ile canlı oturum başlat"
                  >
                    <PlugZap size={14} />
                    <span>{connectingId === selectedDevice.id ? "Bağlanıyor..." : "Canlı Masaüstü"}</span>
                  </button>

                  {isVersionOlder(selectedDevice.agentVersion, latestAgentVersion) && (
                    <button
                      className="btn-hero-update"
                      onClick={() => handleUpdateAgent(selectedDevice.id)}
                      disabled={!selectedDevice.isOnline || updatingDeviceId === selectedDevice.id}
                      title="Ajanı en son sürüme güncelle"
                    >
                      <Sparkles size={13} />
                      <span>Güncelle</span>
                    </button>
                  )}

                  <button
                    className="detail-nav-action-btn"
                    onClick={() => refresh(true)}
                    title="Verileri Yenile"
                  >
                    <RefreshCw size={13} className={loading ? "animate-spin" : ""} />
                    <span>Yenile</span>
                  </button>

                  <button
                    className="detail-nav-action-btn danger"
                    onClick={() => handleDeleteDevice(selectedDevice.id, selectedDevice.deviceName)}
                    title="Cihazı Sistemden Sil"
                  >
                    <Trash2 size={13} />
                  </button>
                </div>
              </div>

              {/* Sub-row: Quick Telemetry Pills */}
              <div className="detail-command-pills-row">
                <span className="command-pill-tag">
                  <Laptop size={12} />
                  <span>{formatOsName(selectedDevice.operatingSystem)}</span>
                </span>

                <span className="command-pill-tag mono">
                  <Globe size={12} />
                  <span>{selectedDevice.ipAddress || "—"}</span>
                  {selectedDevice.ipAddress && (
                    <button
                      className="copy-mini-btn"
                      onClick={() => copyToClipboard(selectedDevice.ipAddress!, "IP Adresi")}
                      title="IP Kopyala"
                    >
                      <Copy size={9} />
                    </button>
                  )}
                </span>

                <span className="command-pill-tag">
                  <User size={12} />
                  <span>{cleanUserName(selectedDevice.activeUser)}</span>
                </span>

                <span className="command-pill-tag">
                  <Radio size={12} />
                  <span>v{selectedDevice.agentVersion}</span>
                </span>

                {isVersionOlder(selectedDevice.agentVersion, latestAgentVersion) && (
                  <span className="badge-warn-hero">
                    <AlertCircle size={11} />
                    v{latestAgentVersion} mevcut
                  </span>
                )}
              </div>
            </div>

            {/* Segmented Navigation Tabs */}
            <div className="detail-segmented-tabs">
              <button
                className={`segmented-tab-btn ${activeDetailTab === "overview" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("overview")}
              >
                <LayoutDashboard size={15} />
                <span>Genel Bakış</span>
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "specs" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("specs")}
              >
                <Sliders size={15} />
                <span>Cihaz Özellikleri</span>
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "network" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("network")}
              >
                <Wifi size={15} />
                <span>Ağ &amp; Bağdaştırıcılar</span>
                {selectedDevice.networkAdapters && selectedDevice.networkAdapters.length > 0 && (
                  <span className="tab-count-pill">{selectedDevice.networkAdapters.length}</span>
                )}
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "applications" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("applications")}
              >
                <Package size={15} />
                <span>Yüklü Uygulamalar</span>
                {selectedDevice.installedApps && selectedDevice.installedApps.length > 0 && (
                  <span className="tab-count-pill">{selectedDevice.installedApps.length}</span>
                )}
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "updates" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("updates")}
              >
                <ShieldCheck size={15} />
                <span>Windows Güncellemeleri</span>
                {selectedDevice.windowsUpdates && selectedDevice.windowsUpdates.length > 0 && (
                  <span className="tab-count-pill">{selectedDevice.windowsUpdates.length}</span>
                )}
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "terminal" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("terminal")}
              >
                <Terminal size={15} />
                <span>Uzak Terminal</span>
              </button>
              <button
                className={`segmented-tab-btn ${activeDetailTab === "activity" ? "active" : ""}`}
                onClick={() => setActiveDetailTab("activity")}
              >
                <Activity size={15} />
                <span>Aktivite &amp; Denetim</span>
              </button>
            </div>

            {/* Tab Contents Body */}
            <div className="detail-page-body">
              {activeDetailTab === "overview" && (
                <div className="bento-overview-layout">
                  {/* Bento Card 1: Sistem & Kimlik */}
                  <div className="bento-card">
                    <div className="bento-card-header">
                      <div className="bento-header-icon blue">
                        <Laptop size={16} />
                      </div>
                      <div className="bento-header-title">
                        <h3>Sistem &amp; Donanım</h3>
                        <p>İşletim sistemi ve donanım kimlik detayları</p>
                      </div>
                    </div>
                    <div className="bento-card-body">
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Cihaz Adı (Hostname)</span>
                        <span className="bento-spec-value font-bold">{selectedDevice.deviceName}</span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">İşletim Sistemi</span>
                        <span className="bento-spec-value font-bold">{formatOsName(selectedDevice.operatingSystem)}</span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Cihaz Kimliği (UUID)</span>
                        <span className="bento-spec-value mono-text mono-xs copyable" onClick={() => copyToClipboard(selectedDevice.id, "Cihaz ID")} title="Cihaz kimliğini kopyala">
                          {selectedDevice.id}
                          <Copy size={11} className="copy-hint-icon" />
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Yüklü Ajan Sürümü</span>
                        <span className="bento-spec-value">
                          <span className="version-pill">v{selectedDevice.agentVersion}</span>
                          {isVersionOlder(selectedDevice.agentVersion, latestAgentVersion) && (
                            <span className="update-available-text"> (v{latestAgentVersion} mevcut)</span>
                          )}
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Son Canlılık Sinyali</span>
                        <span className="bento-spec-value">
                          {selectedDevice.isOnline ? "🟢 Şimdi (Çevrimiçi)" : `⚪ ${formatLastSeen(selectedDevice.lastSeenAt)}`}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Bento Card 2: Oturum & Lokasyon */}
                  <div className="bento-card">
                    <div className="bento-card-header">
                      <div className="bento-header-icon purple">
                        <User size={16} />
                      </div>
                      <div className="bento-header-title">
                        <h3>Oturum &amp; Güvenlik</h3>
                        <p>Kullanıcı hesabı, domain ve lokasyon</p>
                      </div>
                    </div>
                    <div className="bento-card-body">
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Aktif Oturum Kullanıcısı</span>
                        <span className="bento-spec-value font-bold">{cleanUserName(selectedDevice.activeUser)}</span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Domain / Çalışma Grubu</span>
                        <span className="bento-spec-value">{selectedDevice.domainName || "WORKGROUP"}</span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Lokasyon Kodu</span>
                        <span className="bento-spec-value">
                          <span className="location-tag">{selectedDevice.locationCode || "OFFICE"}</span>
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Bağlantı Protokolü</span>
                        <span className="bento-spec-value">WSS / TLS 1.3 (Şifreli)</span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Yönetici İzinleri</span>
                        <span className="bento-spec-value">
                          <span className="shield-tag">🛡️ LocalSystem Destekli</span>
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Bento Card 3: Canlı Kaynak Kullanımı Mini Gauges */}
                  <div className="bento-card">
                    <div className="bento-card-header">
                      <div className="bento-header-icon emerald">
                        <Cpu size={16} />
                      </div>
                      <div className="bento-header-title">
                        <h3>Canlı Donanım Metrikleri</h3>
                        <p>İşlemci, bellek ve depolama doluluk oranları</p>
                      </div>
                    </div>
                    <div className="bento-card-body bento-gauges-body">
                      {/* CPU Mini Gauge */}
                      <div className="mini-gauge-card">
                        <div className="mini-gauge-head">
                          <span className="mini-gauge-title">CPU Kullanımı</span>
                          <span className="mini-gauge-percent font-bold">%{selectedDevice.cpuUsagePercent || 0}</span>
                        </div>
                        <div className="mini-gauge-track">
                          <div
                            className="mini-gauge-bar-fill"
                            style={{ width: `${Math.min(100, Math.max(3, selectedDevice.cpuUsagePercent || 0))}%` }}
                            data-tone={(selectedDevice.cpuUsagePercent || 0) > 85 ? "danger" : (selectedDevice.cpuUsagePercent || 0) > 60 ? "warn" : "primary"}
                          />
                        </div>
                        <span className="mini-gauge-subtext">10 dakikalık kayan pencere ortalaması</span>
                      </div>

                      {/* RAM Mini Gauge */}
                      <div className="mini-gauge-card">
                        <div className="mini-gauge-head">
                          <span className="mini-gauge-title">RAM (Bellek)</span>
                          <span className="mini-gauge-percent font-bold">
                            {((selectedDevice.memoryUsedMb || 0) / 1024).toFixed(1)} / {((selectedDevice.memoryTotalMb || 0) / 1024).toFixed(1)} GB
                          </span>
                        </div>
                        <div className="mini-gauge-track">
                          <div
                            className="mini-gauge-bar-fill"
                            style={{
                              width: `${Math.min(100, Math.max(3, ((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) * 100))}%`
                            }}
                            data-tone={((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) > 0.85 ? "danger" : ((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) > 0.70 ? "warn" : "primary"}
                          />
                        </div>
                        <span className="mini-gauge-subtext">
                          Fiziksel bellek kullanımı: %{selectedDevice.memoryTotalMb ? Math.round(((selectedDevice.memoryUsedMb || 0) / selectedDevice.memoryTotalMb) * 100) : 0}
                        </span>
                      </div>

                      {/* Disk Mini Gauge */}
                      <div className="mini-gauge-card">
                        <div className="mini-gauge-head">
                          <span className="mini-gauge-title">Boş Disk Alanı (C:)</span>
                          <span className="mini-gauge-percent font-bold">{((selectedDevice.diskFreeMb || 0) / 1024).toFixed(1)} GB Boş</span>
                        </div>
                        <div className="mini-gauge-track">
                          <div
                            className="mini-gauge-bar-fill"
                            style={{ width: "65%" }}
                            data-tone="primary"
                          />
                        </div>
                        <span className="mini-gauge-subtext">Sistem sürücüsü alanı sağlıklı</span>
                      </div>
                    </div>
                  </div>

                  {/* Bento Card 4: Ağ & Bağlantı Özeti */}
                  <div className="bento-card">
                    <div className="bento-card-header">
                      <div className="bento-header-icon orange">
                        <Wifi size={16} />
                      </div>
                      <div className="bento-header-title">
                        <h3>Ağ &amp; Bağlantı Özeti</h3>
                        <p>IP adresi ve ağ kartı detayları</p>
                      </div>
                    </div>
                    <div className="bento-card-body">
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Birincil IPv4 Adresi</span>
                        <span className="bento-spec-value mono-text font-bold copyable" onClick={() => selectedDevice.ipAddress && copyToClipboard(selectedDevice.ipAddress, "IP Adresi")} title="IP adresini kopyala">
                          {selectedDevice.ipAddress || "—"}
                          {selectedDevice.ipAddress && <Copy size={11} className="copy-hint-icon" />}
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Ağ Bağdaştırıcıları</span>
                        <span className="bento-spec-value">
                          {selectedDevice.networkAdapters?.length || 0} adet yapılandırılmış kart
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Yüklü Program Envanteri</span>
                        <span className="bento-spec-value">
                          {selectedDevice.installedApps?.length || 0} adet kayıtlı uygulama
                        </span>
                      </div>
                      <div className="bento-spec-item">
                        <span className="bento-spec-label">Ajan Sinyal Durumu</span>
                        <span className="bento-spec-value">
                          {selectedDevice.isOnline ? "🟢 Aktif WebSocket Bağlantısı" : "⚪ Çevrimdışı (Beklemede)"}
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {(activeDetailTab === "specs" || activeDetailTab === "performance") && (
                <div className="device-specs-container">
                  {!selectedDevice.isOnline && (
                    <div className="stale-data-notice" style={{ gridColumn: "1 / -1" }}>
                      <AlertCircle size={15} />
                      <span>Cihaz çevrimdışı — aşağıdaki donanım ve sistem özellikleri son bağlantı anına ({formatLastSeen(selectedDevice.lastSeenAt)}) aittir.</span>
                    </div>
                  )}

                  <div className="specs-categories-grid">
                    {/* Kart 1: Cihaz Kimliği, Anakart & Seri Numaraları */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge blue">
                          <Laptop size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">Cihaz Kimliği &amp; Seri Numaraları</h3>
                          <p className="specs-card-subtitle">Kasa, anakart ve BIOS seri numaraları</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">Cihaz Seri Numarası</span>
                          <span className="specs-val">
                            {selectedDevice.hardwareDetails?.systemSerialNumber || selectedDevice.serialNumber ? (
                              <button
                                type="button"
                                className="hw-serial-badge"
                                onClick={() => copyToClipboard(selectedDevice.hardwareDetails?.systemSerialNumber || selectedDevice.serialNumber || "", "Cihaz Seri No")}
                                title="Seri numarasını kopyalamak için tıklayın"
                              >
                                <span>{selectedDevice.hardwareDetails?.systemSerialNumber || selectedDevice.serialNumber}</span>
                                <Copy size={11} />
                              </button>
                            ) : (
                              <span className="muted-cell">O.E.M. Tanımlı Değil</span>
                            )}
                          </span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Sistem Üreticisi &amp; Model</span>
                          <span className="specs-val font-bold">
                            {selectedDevice.hardwareDetails?.systemManufacturer || "Bilinmeyen Üretici"} {selectedDevice.hardwareDetails?.systemModel ? `· ${selectedDevice.hardwareDetails.systemModel}` : ""}
                          </span>
                        </div>
                        {selectedDevice.hardwareDetails?.systemUuid && (
                          <div className="specs-row">
                            <span className="specs-lbl">Sistem UUID</span>
                            <span className="specs-val mono-text" style={{ fontSize: "11px" }}>{selectedDevice.hardwareDetails.systemUuid}</span>
                          </div>
                        )}
                        <div className="specs-row">
                          <span className="specs-lbl">Anakart (BaseBoard)</span>
                          <span className="specs-val">
                            {selectedDevice.hardwareDetails?.motherboardManufacturer || ""} {selectedDevice.hardwareDetails?.motherboardProduct || "Anakart Modeli"}
                          </span>
                        </div>
                        {selectedDevice.hardwareDetails?.motherboardSerialNumber && (
                          <div className="specs-row">
                            <span className="specs-lbl">Anakart Seri No</span>
                            <span className="specs-val">
                              <button
                                type="button"
                                className="hw-serial-badge"
                                onClick={() => copyToClipboard(selectedDevice.hardwareDetails?.motherboardSerialNumber || "", "Anakart Seri No")}
                              >
                                <span>{selectedDevice.hardwareDetails.motherboardSerialNumber}</span>
                                <Copy size={11} />
                              </button>
                            </span>
                          </div>
                        )}
                        <div className="specs-row">
                          <span className="specs-lbl">BIOS Sürümü &amp; Tarihi</span>
                          <span className="specs-val">
                            {selectedDevice.hardwareDetails?.biosVersion || "UEFI / BIOS"} {selectedDevice.hardwareDetails?.biosReleaseDate ? `(${selectedDevice.hardwareDetails.biosReleaseDate})` : ""}
                          </span>
                        </div>
                        {selectedDevice.hardwareDetails?.biosSerialNumber && (
                          <div className="specs-row">
                            <span className="specs-lbl">BIOS Seri No</span>
                            <span className="specs-val">
                              <button
                                type="button"
                                className="hw-serial-badge"
                                onClick={() => copyToClipboard(selectedDevice.hardwareDetails?.biosSerialNumber || "", "BIOS Seri No")}
                              >
                                <span>{selectedDevice.hardwareDetails.biosSerialNumber}</span>
                                <Copy size={11} />
                              </button>
                            </span>
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Kart 2: İşlemci (CPU) & Mimari */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge blue">
                          <Cpu size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">İşlemci (CPU) &amp; Mimari</h3>
                          <p className="specs-card-subtitle">İşlemci kimliği, çekirdekler ve yük</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">İşlemci Modeli</span>
                          <span className="specs-val font-bold">
                            {selectedDevice.hardwareDetails?.cpuName || "64-bit İşlemci (x64)"}
                          </span>
                        </div>
                        {selectedDevice.hardwareDetails?.cpuProcessorId && (
                          <div className="specs-row">
                            <span className="specs-lbl">İşlemci Kimliği (ID)</span>
                            <span className="specs-val">
                              <button
                                type="button"
                                className="hw-serial-badge"
                                onClick={() => copyToClipboard(selectedDevice.hardwareDetails?.cpuProcessorId || "", "İşlemci Kimliği")}
                              >
                                <span>{selectedDevice.hardwareDetails.cpuProcessorId}</span>
                                <Copy size={11} />
                              </button>
                            </span>
                          </div>
                        )}
                        {(selectedDevice.hardwareDetails?.cpuCores || selectedDevice.hardwareDetails?.cpuLogicalProcessors) && (
                          <div className="specs-row">
                            <span className="specs-lbl">Çekirdek &amp; İş Parçacığı</span>
                            <span className="specs-val font-bold">
                              {selectedDevice.hardwareDetails?.cpuCores || "?"} Fiziksel Çekirdek · {selectedDevice.hardwareDetails?.cpuLogicalProcessors || "?"} Mantıksal İşlemci
                            </span>
                          </div>
                        )}
                        {selectedDevice.hardwareDetails?.cpuMaxClockSpeedMhz && (
                          <div className="specs-row">
                            <span className="specs-lbl">Maksimum Saat Hızı</span>
                            <span className="specs-val mono-text">{selectedDevice.hardwareDetails.cpuMaxClockSpeedMhz} MHz</span>
                          </div>
                        )}
                        <div className="specs-row">
                          <span className="specs-lbl">CPU Ortalama Yükü</span>
                          <div className="specs-val-with-bar">
                            <span className="font-bold mono-text">%{selectedDevice.cpuUsagePercent || 0}</span>
                            <div className="specs-inline-bar">
                              <div
                                className="specs-inline-fill"
                                style={{ width: `${Math.min(100, Math.max(3, selectedDevice.cpuUsagePercent || 0))}%` }}
                                data-tone={(selectedDevice.cpuUsagePercent || 0) > 85 ? "danger" : (selectedDevice.cpuUsagePercent || 0) > 60 ? "warn" : "primary"}
                              />
                            </div>
                          </div>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Sanallaştırma Desteği</span>
                          <span className="specs-val">
                            <span className="spec-badge-pill green">Hyper-V &amp; WSL Uyumlu</span>
                          </span>
                        </div>
                      </div>
                    </div>

                    {/* Kart 3: Fiziksel Bellek (RAM) Modülleri & Slot Seri Numaraları */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge emerald">
                          <Database size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">Bellek (RAM) Modülleri</h3>
                          <p className="specs-card-subtitle">Fiziksel RAM slotları ve modül seri numaraları</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">Toplam Fiziksel RAM</span>
                          <span className="specs-val font-bold mono-text">{((selectedDevice.memoryTotalMb || 0) / 1024).toFixed(1)} GB</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Kullanılan Bellek</span>
                          <div className="specs-val-with-bar">
                            <span className="font-bold mono-text">{((selectedDevice.memoryUsedMb || 0) / 1024).toFixed(1)} GB (%{selectedDevice.memoryTotalMb ? Math.round(((selectedDevice.memoryUsedMb || 0) / selectedDevice.memoryTotalMb) * 100) : 0})</span>
                            <div className="specs-inline-bar">
                              <div
                                className="specs-inline-fill"
                                style={{ width: `${Math.min(100, Math.max(3, ((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) * 100))}%` }}
                                data-tone={((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) > 0.85 ? "danger" : ((selectedDevice.memoryUsedMb || 0) / (selectedDevice.memoryTotalMb || 1)) > 0.70 ? "warn" : "primary"}
                              />
                            </div>
                          </div>
                        </div>

                        {/* Fiziksel RAM Modülleri */}
                        {selectedDevice.hardwareDetails?.ramModules && selectedDevice.hardwareDetails.ramModules.length > 0 && (
                          <div className="hw-component-section">
                            <span className="hw-component-title">Takılı Fiziksel RAM Modülleri ({selectedDevice.hardwareDetails.ramModules.length} Slot)</span>
                            {selectedDevice.hardwareDetails.ramModules.map((ram, rIdx) => (
                              <div key={rIdx} className="hw-sub-card">
                                <div className="hw-sub-header">
                                  <span className="hw-sub-name">
                                    <Database size={13} /> {ram.bankLabel}
                                  </span>
                                  <span className="hw-sub-tag">
                                    {(ram.capacityMb / 1024).toFixed(0)} GB {ram.memoryType || "RAM"} {ram.speedMhz ? `· ${ram.speedMhz} MHz` : ""}
                                  </span>
                                </div>
                                <div className="hw-sub-rows">
                                  {ram.manufacturer && (
                                    <div className="hw-sub-item">
                                      <span className="hw-sub-label">Üretici</span>
                                      <span className="hw-sub-val">{ram.manufacturer}</span>
                                    </div>
                                  )}
                                  {ram.partNumber && (
                                    <div className="hw-sub-item">
                                      <span className="hw-sub-label">Parça No (Part No)</span>
                                      <span className="hw-sub-val mono-text">{ram.partNumber}</span>
                                    </div>
                                  )}
                                  {ram.serialNumber && (
                                    <div className="hw-sub-item" style={{ gridColumn: "1 / -1" }}>
                                      <span className="hw-sub-label">RAM Seri Numarası</span>
                                      <button
                                        type="button"
                                        className="hw-serial-badge"
                                        onClick={() => copyToClipboard(ram.serialNumber || "", "RAM Seri No")}
                                      >
                                        <span>{ram.serialNumber}</span>
                                        <Copy size={11} />
                                      </button>
                                    </div>
                                  )}
                                </div>
                              </div>
                            ))}
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Kart 4: Fiziksel Depolama Sürücüleri (SSD / NVMe / HDD) & Disk Seri Numaraları */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge emerald">
                          <HardDrive size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">Depolama Sürücüleri &amp; Disk Seri No</h3>
                          <p className="specs-card-subtitle">Fiziksel diskler, SSD ve NVMe sürücüleri</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">Sistem Sürücüsü (C:) Boş Alan</span>
                          <span className="specs-val font-bold mono-text">{((selectedDevice.diskFreeMb || 0) / 1024).toFixed(1)} GB Boş</span>
                        </div>

                        {/* Fiziksel Diskler */}
                        {selectedDevice.hardwareDetails?.diskDrives && selectedDevice.hardwareDetails.diskDrives.length > 0 ? (
                          <div className="hw-component-section">
                            <span className="hw-component-title">Fiziksel Depolama Birimleri ({selectedDevice.hardwareDetails.diskDrives.length} Sürücü)</span>
                            {selectedDevice.hardwareDetails.diskDrives.map((disk, dIdx) => (
                              <div key={dIdx} className="hw-sub-card">
                                <div className="hw-sub-header">
                                  <span className="hw-sub-name">
                                    <HardDrive size={13} /> {disk.model}
                                  </span>
                                  <span className="hw-sub-tag">
                                    {disk.sizeGb > 0 ? `${disk.sizeGb} GB` : "Disk"} {disk.interfaceType ? `· ${disk.interfaceType}` : ""}
                                  </span>
                                </div>
                                <div className="hw-sub-rows">
                                  {disk.mediaType && (
                                    <div className="hw-sub-item">
                                      <span className="hw-sub-label">Medya Tipi</span>
                                      <span className="hw-sub-val">{disk.mediaType}</span>
                                    </div>
                                  )}
                                  {disk.partitionsCount !== undefined && disk.partitionsCount !== null && (
                                    <div className="hw-sub-item">
                                      <span className="hw-sub-label">Bölüm Sayısı</span>
                                      <span className="hw-sub-val">{disk.partitionsCount} Bölüm</span>
                                    </div>
                                  )}
                                  {disk.serialNumber && (
                                    <div className="hw-sub-item" style={{ gridColumn: "1 / -1" }}>
                                      <span className="hw-sub-label">Disk Seri Numarası</span>
                                      <button
                                        type="button"
                                        className="hw-serial-badge"
                                        onClick={() => copyToClipboard(disk.serialNumber || "", "Disk Seri No")}
                                      >
                                        <span>{disk.serialNumber}</span>
                                        <Copy size={11} />
                                      </button>
                                    </div>
                                  )}
                                </div>
                              </div>
                            ))}
                          </div>
                        ) : (
                          <div className="specs-row">
                            <span className="specs-lbl">Depolama Tipi</span>
                            <span className="specs-val">Dahili Katı Hal Sürücüsü (SSD / NVMe)</span>
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Kart 5: Ekran Kartları (GPU) */}
                    {selectedDevice.hardwareDetails?.graphicsCards && selectedDevice.hardwareDetails.graphicsCards.length > 0 && (
                      <div className="specs-card">
                        <div className="specs-card-header">
                          <div className="specs-icon-badge purple">
                            <Monitor size={16} />
                          </div>
                          <div>
                            <h3 className="specs-card-title">Ekran Kartları (GPU)</h3>
                            <p className="specs-card-subtitle">Grafik işlemcileri ve video belleği</p>
                          </div>
                        </div>
                        <div className="specs-card-body">
                          {selectedDevice.hardwareDetails.graphicsCards.map((gpu, gIdx) => (
                            <div key={gIdx} className="hw-sub-card">
                              <div className="hw-sub-header">
                                <span className="hw-sub-name">
                                  <Monitor size={13} /> {gpu.name}
                                </span>
                                {gpu.vramMb && gpu.vramMb > 0 && (
                                  <span className="hw-sub-tag">{(gpu.vramMb / 1024).toFixed(0)} GB VRAM</span>
                                )}
                              </div>
                              <div className="hw-sub-rows">
                                {gpu.driverVersion && (
                                  <div className="hw-sub-item">
                                    <span className="hw-sub-label">Sürücü Versiyonu</span>
                                    <span className="hw-sub-val mono-text">{gpu.driverVersion}</span>
                                  </div>
                                )}
                                {gpu.videoProcessor && (
                                  <div className="hw-sub-item">
                                    <span className="hw-sub-label">Video İşlemci</span>
                                    <span className="hw-sub-val">{gpu.videoProcessor}</span>
                                  </div>
                                )}
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}

                    {/* Kart 6: İşletim Sistemi & Ortam */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge purple">
                          <Laptop size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">İşletim Sistemi &amp; Ortam</h3>
                          <p className="specs-card-subtitle">İşletim sistemi sürümü ve yapı detayları</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">İşletim Sistemi Adı</span>
                          <span className="specs-val font-bold">{formatOsName(selectedDevice.operatingSystem)}</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">İşletim Sistemi Ailesi</span>
                          <span className="specs-val">Microsoft Windows NT Platformu</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Bilgisayar Adı (Hostname)</span>
                          <span className="specs-val font-bold">{selectedDevice.deviceName}</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Domain / Çalışma Grubu</span>
                          <span className="specs-val">{selectedDevice.domainName || "WORKGROUP"}</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Yüklü Uygulama Sayısı</span>
                          <span className="specs-val">
                            <span className="spec-badge-pill blue">{selectedDevice.installedApps?.length || 0} Adet Program Kayıtlı</span>
                          </span>
                        </div>
                      </div>
                    </div>

                    {/* Kart 7: Ajan, İzinler & Güvenlik Mimarisi */}
                    <div className="specs-card">
                      <div className="specs-card-header">
                        <div className="specs-icon-badge orange">
                          <ShieldCheck size={16} />
                        </div>
                        <div>
                          <h3 className="specs-card-title">Ajan &amp; Güvenlik Yapılandırması</h3>
                          <p className="specs-card-subtitle">Servis yetkileri, UAC ve iletişim güvenliği</p>
                        </div>
                      </div>
                      <div className="specs-card-body">
                        <div className="specs-row">
                          <span className="specs-lbl">Yüklü Ajan Versiyonu</span>
                          <span className="specs-val">
                            <span className="version-pill">v{selectedDevice.agentVersion}</span>
                          </span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Arka Plan Servis Yetkisi</span>
                          <span className="specs-val font-bold">
                            <span className="shield-tag">NT AUTHORITY\SYSTEM (LocalSystem)</span>
                          </span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">UAC Güvenli Masaüstü</span>
                          <span className="specs-val">PromptOnSecureDesktop = 0 (Uzaktan Onaylanabilir)</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">Yazılımsal SAS (Ctrl+Alt+Del)</span>
                          <span className="specs-val">SoftwareSASGeneration = 3 (Tam Yetkili)</span>
                        </div>
                        <div className="specs-row">
                          <span className="specs-lbl">İletişim Güvenliği</span>
                          <span className="specs-val">WSS (TLS 1.3) + DPAPI Şifreli Kimlik Depolama</span>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {activeDetailTab === "network" && (
                <div className="network-tab-content">
                  {!selectedDevice.isOnline && (
                    <div className="stale-data-notice">
                      Cihaz çevrimdışı — aşağıdaki ağ bilgileri son görülme anına ({formatLastSeen(selectedDevice.lastSeenAt)}) aittir.
                    </div>
                  )}

                  <div className="spec-row" style={{ padding: "12px", background: "var(--bg-canvas)", borderRadius: "var(--radius-control)", border: "1px solid var(--border-color)" }}>
                    <span className="spec-label">Birincil IPv4 Adresi</span>
                    <span className="spec-value mono-text font-bold" style={{ fontSize: "14px" }}>
                      {selectedDevice.ipAddress || "—"}
                      {selectedDevice.ipAddress && (
                        <button
                          className="copy-inline-btn"
                          onClick={() => copyToClipboard(selectedDevice.ipAddress!, "IP Adresi")}
                          aria-label="IP Kopyala"
                        >
                          <Copy size={11} />
                        </button>
                      )}
                    </span>
                  </div>

                  {!selectedDevice.networkAdapters || selectedDevice.networkAdapters.length === 0 ? (
                    <div className="empty-adapters-notice">
                      <Wifi size={24} style={{ margin: "0 auto 8px", opacity: 0.5 }} />
                      <div>Detaylı ağ bağdaştırıcısı verisi henüz alınmadı.</div>
                      <div className="hint-xs">Cihazdan bir sonraki heartbeat sinyali bekleniyor.</div>
                    </div>
                  ) : (
                    <div className="adapters-list">
                      {selectedDevice.networkAdapters.map((adapter, idx) => (
                        <div key={idx} className="adapter-card">
                          <div className="adapter-header">
                            <div className="adapter-title-row">
                              <span className="adapter-name">{adapter.name}</span>
                              <span className={`adapter-badge ${adapter.status.toLowerCase() === "up" ? "up" : "down"}`}>
                                {adapter.status.toLowerCase() === "up" ? "Aktif" : "Bağlı Değil"}
                              </span>
                            </div>
                            <div className="adapter-desc">{adapter.description}</div>
                          </div>

                          <div className="adapter-details">
                            <div className="adapter-row">
                              <span className="adapter-lbl">Tür / Hız:</span>
                              <span className="adapter-val">
                                {adapter.type} {adapter.speedMbps > 0 ? `· ${adapter.speedMbps} Mbps` : ""}
                              </span>
                            </div>

                            <div className="adapter-row">
                              <span className="adapter-lbl">MAC Adresi:</span>
                              <span className="adapter-val mono-text">
                                {adapter.macAddress}
                                {adapter.macAddress !== "-" && (
                                  <button
                                    className="copy-inline-btn"
                                    onClick={() => copyToClipboard(adapter.macAddress, "MAC Adresi")}
                                    aria-label="MAC Kopyala"
                                  >
                                    <Copy size={10} />
                                  </button>
                                )}
                              </span>
                            </div>

                            {adapter.ipAddresses && adapter.ipAddresses.length > 0 && (
                              <div className="adapter-row">
                                <span className="adapter-lbl">IP / Maske:</span>
                                <div className="adapter-val-list mono-text">
                                  {adapter.ipAddresses.map((ip, i) => (
                                    <div key={i}>{ip}</div>
                                  ))}
                                </div>
                              </div>
                            )}

                            {adapter.gateways && adapter.gateways.length > 0 && (
                              <div className="adapter-row">
                                <span className="adapter-lbl">Ağ Geçidi (GW):</span>
                                <span className="adapter-val mono-text">{adapter.gateways.join(", ")}</span>
                              </div>
                            )}

                            {adapter.dnsServers && adapter.dnsServers.length > 0 && (
                              <div className="adapter-row">
                                <span className="adapter-lbl">DNS Sunucuları:</span>
                                <span className="adapter-val mono-text">{adapter.dnsServers.join(", ")}</span>
                              </div>
                            )}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {activeDetailTab === "applications" && (
                <div className="apps-tab-content">
                  {!selectedDevice.isOnline && (
                    <div className="stale-data-notice">
                      Cihaz çevrimdışı — aşağıdaki uygulama listesi son görülme anına ({formatLastSeen(selectedDevice.lastSeenAt)}) aittir.
                    </div>
                  )}

                  {/* Search Bar & Count */}
                  <div className="apps-toolbar">
                    <div className="apps-search-box">
                      <Search size={14} className="search-icon" />
                      <input
                        type="text"
                        placeholder="Uygulama adı, sürüm veya yayımcı ara..."
                        value={appSearchQuery}
                        onChange={(e) => setAppSearchQuery(e.target.value)}
                      />
                      {appSearchQuery && (
                        <button className="clear-search-btn" onClick={() => setAppSearchQuery("")}>✕</button>
                      )}
                    </div>
                    <div className="apps-count-badge">
                      Toplam <strong>{selectedDevice.installedApps?.length || 0}</strong> yüklü uygulama
                    </div>
                  </div>

                  {!selectedDevice.installedApps || selectedDevice.installedApps.length === 0 ? (
                    <div className="empty-adapters-notice">
                      <Package size={28} style={{ margin: "0 auto 8px", opacity: 0.5 }} />
                      <div>Yüklü uygulama envanteri henüz toplanmadı.</div>
                      <div className="hint-xs">Cihazdan bir sonraki heartbeat sinyali bekleniyor.</div>
                    </div>
                  ) : (
                    <div className="apps-table-container">
                      <table className="apps-table">
                        <thead>
                          <tr>
                            <th>Uygulama Adı</th>
                            <th>Yayımcı</th>
                            <th>Sürüm</th>
                            <th>Yükleme Tarihi</th>
                            <th>Boyut</th>
                            <th style={{ textAlign: "right", width: "130px" }}>İşlem</th>
                          </tr>
                        </thead>
                        <tbody>
                          {filteredApps.length === 0 ? (
                            <tr>
                              <td colSpan={6} className="empty-table-cell">
                                Arama kriterine uygun uygulama bulunamadı.
                              </td>
                            </tr>
                          ) : (
                            filteredApps.map((app, idx) => (
                              <tr key={idx}>
                                <td>
                                  <div className="app-name-cell">
                                    <span className="app-icon"><Package size={13} /></span>
                                    <span className="app-name-text">{app.name}</span>
                                  </div>
                                </td>
                                <td>
                                  <span className="app-publisher-cell">{app.publisher || "—"}</span>
                                </td>
                                <td>
                                  <span className="mono-text mono-xs app-version-badge">{app.version || "—"}</span>
                                </td>
                                <td>
                                  <span className="dim-time">{app.installDate || "—"}</span>
                                </td>
                                <td>
                                  <span className="mono-text mono-xs">
                                    {app.estimatedSizeKb && app.estimatedSizeKb > 0
                                      ? app.estimatedSizeKb > 1024
                                        ? `${(app.estimatedSizeKb / 1024).toFixed(1)} MB`
                                        : `${app.estimatedSizeKb} KB`
                                      : "—"}
                                  </span>
                                </td>
                                <td style={{ textAlign: "right" }}>
                                  <button
                                    className="btn-silent-uninstall"
                                    onClick={() => {
                                      setUninstallResult(null);
                                      setUninstallingApp(app);
                                    }}
                                    disabled={!selectedDevice.isOnline || isUninstalling}
                                    title="Uygulamayı hedef bilgisayardan sessizce kaldır"
                                  >
                                    <Trash2 size={12} />
                                    <span>Sessiz Kaldır</span>
                                  </button>
                                </td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}

              {activeDetailTab === "updates" && (
                <div className="updates-tab-content">
                  {!selectedDevice.isOnline && (
                    <div className="stale-data-notice">
                      ⚠️ Cihaz çevrimdışı. Görüntülenen güncelleme listesi son heartbeat zamanına ({formatLastSeen(selectedDevice.lastSeenAt)}) aittir.
                    </div>
                  )}

                  {/* Summary Metric Cards */}
                  <div className="updates-summary-grid">
                    <div className="update-stat-card">
                      <div className="update-stat-icon blue">
                        <ShieldCheck size={20} />
                      </div>
                      <div className="update-stat-info">
                        <span className="update-stat-label">Yüklü Güncelleştirme</span>
                        <span className="update-stat-value">{selectedDevice.windowsUpdates?.length ?? 0} KB Paketi</span>
                      </div>
                    </div>

                    <div className="update-stat-card">
                      <div className="update-stat-icon green">
                        <Clock size={20} />
                      </div>
                      <div className="update-stat-info">
                        <span className="update-stat-label">Son Güncelleme Zamanı</span>
                        <span className="update-stat-value">
                          {selectedDevice.windowsUpdates?.[0]?.installedOn || "Bilinmiyor"}
                        </span>
                      </div>
                    </div>

                    <div className="update-stat-card">
                      <div className="update-stat-icon purple">
                        <Server size={20} />
                      </div>
                      <div className="update-stat-info">
                        <span className="update-stat-label">İşletim Sistemi</span>
                        <span className="update-stat-value">{formatOsName(selectedDevice.operatingSystem)}</span>
                      </div>
                    </div>

                    <div className="update-stat-card">
                      <div className="update-stat-icon orange">
                        <Radio size={20} />
                      </div>
                      <div className="update-stat-info">
                        <span className="update-stat-label">Windows Update Servisi</span>
                        <span className="update-stat-value">wuauserv (Aktif)</span>
                      </div>
                    </div>
                  </div>

                  {/* Toolbar & Search */}
                  <div className="updates-toolbar">
                    <div className="apps-search-box">
                      <Search size={14} className="search-icon" />
                      <input
                        type="text"
                        placeholder="KB numarası (Örn: KB503...), açıklama veya yükleyen ara..."
                        value={updateSearchQuery}
                        onChange={(e) => setUpdateSearchQuery(e.target.value)}
                      />
                      {updateSearchQuery && (
                        <button className="clear-search-btn" onClick={() => setUpdateSearchQuery("")}>✕</button>
                      )}
                    </div>

                    <div className="updates-actions-group">
                      <button
                        className="btn-quick-update-action"
                        onClick={() => {
                          setActiveDetailTab("terminal");
                          handleRunTerminalCommand("Get-HotFix | Select-Object -First 15 HotFixID, Description, InstalledOn, InstalledBy");
                        }}
                        title="Hedef makinede PowerShell Get-HotFix çalıştırarak canlı güncellemeleri terminalde sorgula"
                        disabled={!selectedDevice.isOnline}
                      >
                        <Search size={13} />
                        <span>Canlı Güncellemeleri Tara</span>
                      </button>

                      <button
                        className="btn-quick-update-action secondary"
                        onClick={() => {
                          setActiveDetailTab("terminal");
                          handleRunTerminalCommand("Restart-Service wuauserv -Force; Get-Service wuauserv");
                        }}
                        title="Windows Update servisini (wuauserv) yeniden başlat"
                        disabled={!selectedDevice.isOnline}
                      >
                        <RefreshCw size={13} />
                        <span>Update Servisini Yeniden Başlat</span>
                      </button>
                    </div>
                  </div>

                  {!selectedDevice.windowsUpdates || selectedDevice.windowsUpdates.length === 0 ? (
                    <div className="empty-adapters-notice">
                      <ShieldCheck size={28} style={{ margin: "0 auto 8px", opacity: 0.5 }} />
                      <div>İşletim sistemi güncelleştirme envanteri henüz toplanmadı.</div>
                      <div className="hint-xs">Cihazdan bir sonraki heartbeat sinyali bekleniyor veya yukarıdaki 'Canlı Güncellemeleri Tara' butonunu kullanabilirsiniz.</div>
                    </div>
                  ) : (
                    <div className="apps-table-container">
                      <table className="apps-table">
                        <thead>
                          <tr>
                            <th>KB / Güncelleştirme No</th>
                            <th>Açıklama / Sürüm Türü</th>
                            <th>Yükleme Tarihi</th>
                            <th>Yükleyen / Kaynak</th>
                            <th style={{ textAlign: "right" }}>Durum</th>
                          </tr>
                        </thead>
                        <tbody>
                          {filteredUpdates.length === 0 ? (
                            <tr>
                              <td colSpan={5} className="empty-table-cell">
                                Arama kriterine uygun güncelleştirme bulunamadı.
                              </td>
                            </tr>
                          ) : (
                            filteredUpdates.map((upd, idx) => (
                              <tr key={idx}>
                                <td>
                                  <div className="app-name-cell">
                                    <span className="app-icon update-kb-icon"><Shield size={13} /></span>
                                    <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                                      <span className="app-name-text font-bold">{upd.hotFixId}</span>
                                      {upd.supportUrl && (
                                        <a
                                          href={upd.supportUrl}
                                          target="_blank"
                                          rel="noreferrer"
                                          className="kb-link-icon"
                                          title="Microsoft Destek Makalesini Aç"
                                        >
                                          <ExternalLink size={11} />
                                        </a>
                                      )}
                                    </div>
                                  </div>
                                </td>
                                <td>
                                  <span className="app-publisher-cell">{upd.description || "Güvenlik Güncelleştirmesi"}</span>
                                </td>
                                <td>
                                  <span className="dim-time">{upd.installedOn || "—"}</span>
                                </td>
                                <td>
                                  <span className="mono-text mono-xs">{upd.installedBy || "NT AUTHORITY\\SYSTEM"}</span>
                                </td>
                                <td style={{ textAlign: "right" }}>
                                  <span className={`spec-badge-pill ${upd.status?.includes("Superseded") ? "warn" : "green"}`}>
                                    {upd.status?.includes("Superseded") ? "Üzerine Yazıldı" : "✓ Yüklü"}
                                  </span>
                                </td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                  )}
                </div>
              )}

              {activeDetailTab === "terminal" && (
                <div className="web-terminal-container">
                  {!selectedDevice.isOnline && (
                    <div className="stale-data-notice">
                      ⚠️ Cihaz çevrimdışı. Canlı komut yürütmek için cihazın çevrimiçi olması gerekmektedir.
                    </div>
                  )}

                  {/* Shell Selector & Options */}
                  <div className="terminal-control-bar">
                    <div className="terminal-shell-selector">
                      <button
                        className={`shell-tab-btn ${terminalShell === "cmd" ? "active" : ""}`}
                        onClick={() => setTerminalShell("cmd")}
                      >
                        <Terminal size={14} />
                        Komut İstemi (CMD)
                      </button>
                      <button
                        className={`shell-tab-btn ${terminalShell === "powershell" ? "active" : ""}`}
                        onClick={() => setTerminalShell("powershell")}
                      >
                        <Zap size={14} />
                        Windows PowerShell
                      </button>
                    </div>

                    <div className="terminal-admin-badge-permanent" title="Komutlar hedef makinede her zaman tam SYSTEM / Yönetici ayrıcalığı ile yürütülür">
                      <ShieldCheck size={14} />
                      <span>Yönetici Yetkili (NT AUTHORITY\SYSTEM)</span>
                    </div>
                  </div>

                  {/* Quick Commands Bar */}
                  <div className="quick-commands-bar">
                    <span className="quick-cmd-label">Hızlı Komutlar:</span>
                    {terminalShell === "cmd" ? (
                      <>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("ipconfig /all")}>ipconfig /all</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("whoami /all")}>whoami /all</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("netstat -ano")}>netstat -ano</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("systeminfo")}>systeminfo</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("net user")}>net user</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("gpupdate /force")}>gpupdate /force</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("hostname")}>hostname</button>
                      </>
                    ) : (
                      <>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("Get-Service | Where-Object Status -eq Running | Select-Object -First 15")}>Get-Service (Aktif)</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("Get-Process | Sort-Object CPU -Descending | Select-Object -First 10 ProcessName, Id, CPU, WorkingSet64")}>Top 10 CPU Süreci</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("Get-NetIPAddress | Select-Object IPAddress, InterfaceAlias, AddressFamily")}>Get-NetIPAddress</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("Get-Volume")}>Get-Volume</button>
                        <button className="quick-cmd-chip" onClick={() => handleRunTerminalCommand("Get-ComputerInfo | Select-Object WindowsProductName, CsManufacturer, CsModel, TotalPhysicalMemory")}>Get-ComputerInfo</button>
                      </>
                    )}
                  </div>

                  {/* Terminal Window Emulator */}
                  <div className="terminal-window">
                    <div className="terminal-window-header">
                      <div className="terminal-window-title">
                        <Terminal size={14} />
                        <span>
                          {selectedDevice.deviceName} — {terminalShell === "cmd" ? "cmd.exe" : "powershell.exe"}
                        </span>
                        <span className="admin-badge">Yönetici (SYSTEM)</span>
                      </div>
                      <div className="terminal-window-actions">
                        <button
                          className="term-action-btn"
                          onClick={() => {
                            const allText = terminalLogs.map(l => `> ${l.command}\n${l.stdOut || l.stdErr}`).join("\n\n");
                            navigator.clipboard.writeText(allText);
                          }}
                          title="Tüm konsol çıktısını kopyala"
                        >
                          <Copy size={11} /> Kopyala
                        </button>
                        <button
                          className="term-action-btn"
                          onClick={() => setTerminalLogs([])}
                          title="Konsol ekranını temizle"
                        >
                          <Trash2 size={11} /> Temizle
                        </button>
                      </div>
                    </div>

                    <div className="terminal-output-area">
                      <div className="terminal-welcome">
                        NexMote Uzak Terminal Konsolu [Sürüm {selectedDevice.agentVersion}]<br />
                        Hedef Makine: {selectedDevice.deviceName} ({selectedDevice.ipAddress || "Bilinmiyor"}) · Yetki: Yönetici (NT AUTHORITY\SYSTEM)<br />
                        Doğrudan web üzerinden anlık komut çalıştırmak için komutunuzu yazıp Enter'a basın.
                      </div>

                      {terminalLogs.map((log) => (
                        <div key={log.id} className="terminal-log-block">
                          <div className="terminal-log-prompt">
                            <span>
                              {log.shell === "powershell" ? "PS C:\\Windows\\System32> " : "C:\\Windows\\System32> "}
                              {log.command}
                            </span>
                            <span className={`terminal-log-meta ${log.exitCode === 0 ? "success" : "error"}`}>
                              {log.time} · {log.durationMs} ms · Çıkış Kodu: {log.exitCode}
                            </span>
                          </div>

                          {log.stdOut && (
                            <pre className="terminal-log-content">{log.stdOut}</pre>
                          )}

                          {log.stdErr && (
                            <pre className="terminal-log-error">{log.stdErr}</pre>
                          )}

                          {log.elevationDenied && (
                            <div className="terminal-log-error">⚠️ Yönetici izni reddedildi veya UAC onayı verilmedi.</div>
                          )}

                          {log.timedOut && (
                            <div className="terminal-log-error">⚠️ Komut zaman aşımına uğradı (Timeout).</div>
                          )}
                        </div>
                      ))}

                      {terminalRunning && (
                        <div className="terminal-log-block">
                          <div className="terminal-log-prompt">
                            <span>
                              {terminalShell === "powershell" ? "PS C:\\Windows\\System32> " : "C:\\Windows\\System32> "}
                              <em>Komut hedef cihazda yürütülüyor...</em>
                            </span>
                          </div>
                        </div>
                      )}

                      <div ref={terminalBottomRef} />
                    </div>

                    {/* Input Bar */}
                    <div className="terminal-input-bar">
                      <span className="terminal-prompt-prefix">
                        {terminalShell === "powershell" ? "PS >" : "C:\\>"}
                      </span>
                      <input
                        type="text"
                        className="terminal-input-field"
                        placeholder={terminalShell === "powershell" ? "PowerShell komutu yazın (örn: Get-Service, Restart-Service)..." : "CMD komutu yazın (örn: ipconfig, net user, gpupdate)..."}
                        value={terminalInput}
                        disabled={terminalRunning || !selectedDevice.isOnline}
                        onChange={(e) => setTerminalInput(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") {
                            e.preventDefault();
                            handleRunTerminalCommand();
                          } else if (e.key === "ArrowUp") {
                            if (cmdHistory.length > 0) {
                              const nextIdx = Math.min(cmdHistory.length - 1, historyIndex + 1);
                              setHistoryIndex(nextIdx);
                              setTerminalInput(cmdHistory[nextIdx]);
                            }
                          } else if (e.key === "ArrowDown") {
                            if (historyIndex > 0) {
                              const nextIdx = historyIndex - 1;
                              setHistoryIndex(nextIdx);
                              setTerminalInput(cmdHistory[nextIdx]);
                            } else if (historyIndex === 0) {
                              setHistoryIndex(-1);
                              setTerminalInput("");
                            }
                          }
                        }}
                      />
                      <button
                        className="terminal-run-btn"
                        onClick={() => handleRunTerminalCommand()}
                        disabled={terminalRunning || !terminalInput.trim() || !selectedDevice.isOnline}
                      >
                        {terminalRunning ? (
                          <>
                            <RefreshCw size={13} className="animate-spin" /> Çalışıyor...
                          </>
                        ) : (
                          <>
                            <Play size={13} /> Çalıştır
                          </>
                        )}
                      </button>
                    </div>
                  </div>
                </div>
              )}

              {activeDetailTab === "activity" && (
                <div className="activity-list">
                  {activityLogs.length === 0 ? (
                    <div className="activity-empty">
                      Henüz kayıtlı işlem yok.
                    </div>
                  ) : (
                    activityLogs.map((log) => (
                      <div key={log.id} className={`activity-item ${log.level}`}>
                        <span>{log.text}</span>
                        <time>{log.time}</time>
                      </div>
                    ))
                  )}
                </div>
              )}
            </div>
          </div>
        )}

        {/* View 2: Downloads Package Catalog */}
        {view === "downloads" && (
          <div className="content-pane">
            <div className="content-card">
              <h2 className="content-card-title">Kurulum ve Temizleme Paketleri</h2>
              <p className="content-card-copy">
                Active Directory GPO, SCCM, Intune, elle kurulum veya tek tıkla derin kaldırma için hazır araçlar.
              </p>

              <div className="package-list">
                {downloads.map((pkg) => {
                  const isCleanup = pkg.fileName.toLowerCase().includes("cleanup");
                  const sizeLabel = pkg.sizeBytes > 1024 * 1024
                    ? `${(pkg.sizeBytes / (1024 * 1024)).toFixed(1)} MB`
                    : `${Math.max(1, Math.round(pkg.sizeBytes / 1024))} KB`;

                  return (
                    <div key={pkg.fileName} className={`package-card ${isCleanup ? "cleanup-card" : ""}`}>
                      <div className="package-main">
                        <div className={`package-icon ${isCleanup ? "danger-icon" : ""}`}>
                          {isCleanup ? <Trash2 size={18} /> : <Download size={18} />}
                        </div>
                        <div>
                          <div className="package-name">
                            {pkg.name}
                            {pkg.version && <span className="version-pill"> v{pkg.version}</span>}
                          </div>
                          <div className="mono-text mono-xs">
                            {pkg.fileName} · {sizeLabel} · {pkg.description}
                          </div>
                        </div>
                      </div>

                      <a
                        href={pkg.url}
                        download
                        className={isCleanup ? "btn-secondary btn-danger-subtle" : "btn-secondary"}
                      >
                        <Download size={14} /> İndir
                      </a>
                    </div>
                  );
                })}
              </div>
            </div>

            <div className="content-card">
              <h2 className="content-card-title">Özel Sessiz Kurulum Paketi</h2>
              <div className="stale-data-notice">
                <AlertCircle size={14} />
                <span>
                  İçinde gömülü yönetici kimlik bilgisi bulunur. Sadece hedef cihaza özel, doğrudan bir
                  kanaldan (şifreli e-posta, tek seferlik link) iletin — genel dağıtım listelerine
                  eklemeyin. Kurulum tamamlanınca gömülü hesabın şifresini değiştirin.
                </span>
              </div>
              <p className="content-card-copy">
                Ofis dışındaki, uzak masaüstü erişimi olmayan cihazlara; kullanıcıya şifre söylemeden,
                hiçbir onay penceresi göstermeden ajanı sessizce kuran paket.
              </p>
              <button
                className="btn-secondary"
                onClick={handleDownloadSilentInstaller}
                disabled={silentInstallerDownloading}
              >
                <Download size={14} /> {silentInstallerDownloading ? "İndiriliyor..." : "Sessiz Kurulum Paketini İndir"}
              </button>
            </div>
          </div>
        )}

        {/* View 3: Server Settings */}
        {view === "settings" && (
          <div className="content-pane">
            <div className="content-card">
              <h2 className="content-card-title">Sunucu ve kayıt yapılandırması</h2>
              <p className="content-card-copy">
                Ajan cihazlarının sunucuya kayıt olması ve canlılık sinyali ayarları.
              </p>

              <form onSubmit={handleSaveSettings} className="settings-form">
                <div className="form-group">
                  <label className="form-label">Sunucu adresi</label>
                  <input
                    type="url"
                    className="form-input"
                    value={settings.serverUrl}
                    onChange={(e) => setSettings({ ...settings, serverUrl: e.target.value })}
                    required
                  />
                  <span className="form-help">Ajan ve teknisyen uygulamalarının bağlandığı güvenli alan adı.</span>
                </div>

                <div className="form-group">
                  <label className="form-label">Kayıt anahtarı</label>
                  <input
                    type="password"
                    className="form-input"
                    value={settings.enrollmentKey}
                    onChange={(e) => setSettings({ ...settings, enrollmentKey: e.target.value })}
                    required
                  />
                  <span className="form-help">Yeni istemci kurulumlarında kullanılan yetkilendirme anahtarı.</span>
                </div>

                <div className="form-group">
                  <label className="form-label">Canlılık sinyali sıklığı (saniye)</label>
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
                  <label className="form-label">Varsayılan lokasyon kodu</label>
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
                  data-size="lg"
                  data-width="fixed"
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

      {/* Delete Device Confirmation Modal */}
      {deleteModal && (
        <div className="modal-backdrop" onClick={() => setDeleteModal(null)}>
          <div className="modal-dialog delete-modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div className="modal-title-with-icon">
                <div className="modal-danger-icon">
                  <Trash2 size={20} />
                </div>
                <div>
                  <h3 className="modal-title">
                    {deleteModal.deviceIds.length === 1 ? `"${deleteModal.deviceNames[0]}" Cihazını Sil` : `${deleteModal.deviceIds.length} Cihazı Sil`}
                  </h3>
                  <p className="modal-subtitle">Bu işlem cihaz kaydını web konsolundan kalıcı olarak silecektir.</p>
                </div>
              </div>
              <button className="modal-close-btn" onClick={() => setDeleteModal(null)}>
                <X size={18} />
              </button>
            </div>

            <div className="modal-body">
              <div className="delete-option-card">
                <label className="delete-uninstall-toggle">
                  <input
                    type="checkbox"
                    checked={deleteModal.uninstallAgent}
                    onChange={(e) => setDeleteModal({ ...deleteModal, uninstallAgent: e.target.checked })}
                  />
                  <div className="delete-toggle-text">
                    <span className="delete-toggle-title">
                      🛡️ Hedef Bilgisayardaki NexMote Ajanını da Kaldır (Sessiz Uninstall)
                    </span>
                    <span className="delete-toggle-desc">
                      Seçilirse, hedef makinedeki Windows Servisi, Sistem Tepsisi ve tüm ajan dosyaları arka planda sessizce ve tamamen kaldırılır.
                    </span>
                  </div>
                </label>
              </div>

              {!deleteModal.isOnline && deleteModal.uninstallAgent && (
                <div className="modal-warning-notice">
                  ⚠️ Cihaz şu anda çevrimdışı. Cihaz açıldığında kaldırma sinyali gönderilebilmesi için cihazın çevrimiçi olması önerilir.
                </div>
              )}
            </div>

            <div className="modal-footer">
              <button className="btn-secondary" onClick={() => setDeleteModal(null)}>
                Vazgeç
              </button>
              <button className="btn-danger" onClick={confirmDeleteDevices}>
                <Trash2 size={14} /> Kalıcı Olarak Sil
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Modal: Sessiz Uygulama Kaldırma (Silent Uninstall Modal) */}
      {uninstallingApp && selectedDevice && (
        <div className="modal-backdrop" onClick={() => !isUninstalling && setUninstallingApp(null)}>
          <div className="modal-card modal-md" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div className="modal-title-with-icon">
                <div className="modal-icon-badge danger">
                  <Trash2 size={18} />
                </div>
                <div>
                  <h3 className="modal-title">Sessiz Uygulama Kaldırma</h3>
                  <p className="modal-subtitle">{selectedDevice.deviceName} cihazı üzerinden yazılım kaldırma</p>
                </div>
              </div>
              <button
                className="modal-close-btn"
                onClick={() => !isUninstalling && setUninstallingApp(null)}
                disabled={isUninstalling}
              >
                <X size={16} />
              </button>
            </div>

            <div className="modal-body">
              <div className="uninstall-app-summary-card">
                <div className="uninstall-app-icon">
                  <Package size={24} />
                </div>
                <div className="uninstall-app-meta">
                  <h4 className="uninstall-app-name">{uninstallingApp.name}</h4>
                  <div className="uninstall-app-details">
                    <span>Yayımcı: <strong>{uninstallingApp.publisher || "Bilinmiyor"}</strong></span>
                    <span>·</span>
                    <span>Sürüm: <strong>{uninstallingApp.version || "—"}</strong></span>
                    {uninstallingApp.estimatedSizeKb && (
                      <>
                        <span>·</span>
                        <span>Boyut: <strong>{(uninstallingApp.estimatedSizeKb / 1024).toFixed(1)} MB</strong></span>
                      </>
                    )}
                  </div>
                </div>
              </div>

              <div className="uninstall-notice-box">
                <ShieldCheck size={16} className="notice-icon" />
                <div className="notice-text">
                  <strong>Arka Planda Sessiz Yürütme:</strong> Bu işlem <code>NT AUTHORITY\SYSTEM</code> yetkisiyle doğrudan Windows arka planında yürütülecektir. Kullanıcının ekranında herhangi bir kurulum/onay penceresi çıkmayacaktır.
                </div>
              </div>

              {uninstallResult && (
                <div className={`uninstall-result-card ${uninstallResult.success ? "success" : "error"}`}>
                  <div className="uninstall-result-head">
                    {uninstallResult.success ? <CheckCircle2 size={16} /> : <AlertCircle size={16} />}
                    <span>{uninstallResult.message}</span>
                  </div>
                  {uninstallResult.stdErr && (
                    <pre className="uninstall-result-log">{uninstallResult.stdErr}</pre>
                  )}
                  {uninstallResult.stdOut && (
                    <pre className="uninstall-result-log">{uninstallResult.stdOut}</pre>
                  )}
                </div>
              )}
            </div>

            <div className="modal-footer">
              <button
                className="btn-secondary"
                onClick={() => {
                  setUninstallingApp(null);
                  setUninstallResult(null);
                }}
                disabled={isUninstalling}
              >
                {uninstallResult ? "Kapat" : "Vazgeç"}
              </button>

              {!uninstallResult && (
                <button
                  className="btn-danger"
                  onClick={() => handleUninstallApp(uninstallingApp)}
                  disabled={isUninstalling || !selectedDevice.isOnline}
                >
                  {isUninstalling ? (
                    <>
                      <RefreshCw size={14} className="animate-spin" />
                      <span>Sessizce Kaldırılıyor...</span>
                    </>
                  ) : (
                    <>
                      <Trash2 size={14} />
                      <span>Evet, Sessizce Kaldır</span>
                    </>
                  )}
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Floating Notification Toast */}
      {status && (
        <div className="toast-container" role="status" aria-live="polite" aria-atomic="true">
          {status}
        </div>
      )}
    </div>
  );
}
