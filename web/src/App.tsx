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
  Users as UsersIcon,
  Wifi,
  X,
  Zap,
  Database,
  ExternalLink,
  KeyRound,
  ScrollText,
  Ban,
  RotateCcw,
  Building2,
  ChevronLeft,
  ChevronRight,
  Menu,
  Layers,
  Folder,
  FolderPlus,
  Plus,
  CheckSquare,
  Square
} from "lucide-react";
import React, { Fragment, useEffect, useMemo, useRef, useState } from "react";
import QRCode from "qrcode";
import {
  acceptInvite,
  ActiveDeviceAlert,
  ActivityLogEntry as AuditLogEntry,
  assignDeviceGroup,
  assignSecurityProfile,
  changePassword,
  checkUpdates,
  clearStoredAdminToken,
  createDeviceGroup,
  createRemoteSession,
  createSecurityProfile,
  createUser,
  CurrentUser,
  deleteDevice,
  deleteDeviceGroup,
  deleteSecurityProfile,
  DeviceGroup,
  DeviceGroupInput,
  disableMfa,
  disableUser,
  DeviceSummary,
  DownloadPackage,
  enableMfa,
  enableUser,
  executeDeviceCommand,
  getActiveAlerts,
  getAuditLog,
  getCurrentUser,
  getInvitePreview,
  getServerMetrics,
  getServerSettings,
  getStoredAdminToken,
  InstalledAppInfo,
  inviteUser,
  listDeviceGroups,
  listDevices,
  listDownloads,
  listSecurityProfiles,
  listUsers,
  login,
  logout as apiLogout,
  resetUserMfa,
  SecurityProfile,
  SecurityProfileInput,
  ServerMetrics,
  ServerSettings,
  setStoredAdminToken,
  setupMfa,
  setUserRole,
  testSmtp,
  triggerAgentUpdate,
  uninstallApp,
  updateDeviceGroup,
  updateServerSettings,
  updateSecurityProfile,
  UserSummary,
  verifyMfa,
  WindowsUpdateInfo
} from "./api";
import { DetailTab, SortDirection, SortField, StatusFilter, View } from "./types";
import {
  cleanUserName,
  describeAlertType,
  formatLastSeen,
  formatOsName,
  formatUptime,
  isVersionOlder,
  renderSortIndicator,
  renderSparkline,
} from "./utils";
import { LoginScreen } from "./components/LoginScreen";
import { InviteAcceptScreen } from "./components/InviteAcceptScreen";
import { AppSidebar } from "./components/AppSidebar";
import { AppHeader } from "./components/AppHeader";
import { DownloadsView } from "./components/DownloadsView";
import { AuditLogView } from "./components/AuditLogView";
import { UsersView } from "./components/UsersView";

export function App() {
  const [devices, setDevices] = useState<DeviceSummary[]>([]);
  const [downloads, setDownloads] = useState<DownloadPackage[]>([]);
  const [updatingDeviceId, setUpdatingDeviceId] = useState<string | null>(null);
  const [settings, setSettings] = useState<ServerSettings>({
    serverUrl: "https://nexmote.com",
    enrollmentKey: "dev-enrollment-key",
    heartbeatSeconds: 20,
    defaultLocationCode: "OFFICE",
    smtpPort: 465,
    alertsEnabled: true,
    alertOfflineEnabled: true,
    alertOfflineMinutes: 5,
    alertDiskLowEnabled: true,
    alertDiskLowMb: 5000,
    alertCpuHighEnabled: false,
    alertCpuHighPercent: 90,
    alertMemoryHighEnabled: false,
    alertMemoryHighPercent: 90
  });
  const [activeAlerts, setActiveAlerts] = useState<ActiveDeviceAlert[]>([]);
  const [smtpTestEmail, setSmtpTestEmail] = useState("");
  const [testingSmtp, setTestingSmtp] = useState(false);

  // Server Performance Metrics State
  const [serverMetrics, setServerMetrics] = useState<ServerMetrics | null>(null);
  const [metricsLoading, setMetricsLoading] = useState(false);
  const [cpuHistory, setCpuHistory] = useState<number[]>([]);
  const [netHistory, setNetHistory] = useState<number[]>([]);

  const [view, setView] = useState<View>("devices");
  const [sidebarCollapsed, setSidebarCollapsed] = useState<boolean>(() => {
    return localStorage.getItem("nexmote_sidebar_collapsed") === "true";
  });
  const [selectedGroupId, setSelectedGroupId] = useState<string>("all");
  const [settingsActiveTab, setSettingsActiveTab] = useState<"server" | "alerts" | "account">("server");

  function toggleSidebar() {
    setSidebarCollapsed(prev => {
      const next = !prev;
      localStorage.setItem("nexmote_sidebar_collapsed", String(next));
      return next;
    });
  }

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
  const [currentUser, setCurrentUser] = useState<CurrentUser | null>(null);
  const [loginEmail, setLoginEmail] = useState("");
  const [loginPassword, setLoginPassword] = useState("");
  const [showLoginPassword, setShowLoginPassword] = useState(false);
  const [showEnrollmentKey, setShowEnrollmentKey] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);
  const [authError, setAuthError] = useState("");
  const [isLoggingIn, setIsLoggingIn] = useState(false);

  // MFA giriş adım 2 (challenge) state
  const [mfaChallengeToken, setMfaChallengeToken] = useState<string | null>(null);
  const [mfaCode, setMfaCode] = useState("");
  const [mfaError, setMfaError] = useState("");
  const [mfaVerifying, setMfaVerifying] = useState(false);

  // Hesap Ayarları: şifre değiştirme + MFA kurulum state
  const [accountCurrentPassword, setAccountCurrentPassword] = useState("");
  const [accountNewPassword, setAccountNewPassword] = useState("");
  const [accountConfirmNewPassword, setAccountConfirmNewPassword] = useState("");
  const [accountBusy, setAccountBusy] = useState(false);
  const [mfaSetupQr, setMfaSetupQr] = useState<string | null>(null);
  const [mfaSetupSecret, setMfaSetupSecret] = useState<string | null>(null);
  const [mfaEnableCode, setMfaEnableCode] = useState("");
  const [mfaRecoveryCodes, setMfaRecoveryCodes] = useState<string[] | null>(null);
  const [mfaDisablePassword, setMfaDisablePassword] = useState("");

  // Kullanıcı Yönetimi (Admin) state
  const [users, setUsers] = useState<UserSummary[]>([]);
  const [newUserEmail, setNewUserEmail] = useState("");
  const [newUserDisplayName, setNewUserDisplayName] = useState("");
  const [newUserRole, setNewUserRole] = useState<"Admin" | "Technician">("Technician");
  const [newUserMode, setNewUserMode] = useState<"password" | "invite">("invite");
  const [creatingUser, setCreatingUser] = useState(false);
  const [createdUserCredentials, setCreatedUserCredentials] = useState<{ email: string; temporaryPassword: string } | null>(null);
  const [invitedEmail, setInvitedEmail] = useState<string | null>(null);

  // Davet Kabul ekranı state (public — URL'de /invite/{token} ile tetiklenir)
  const inviteToken = useMemo(() => {
    const match = window.location.pathname.match(/^\/invite\/([^/]+)/);
    return match ? match[1] : null;
  }, []);
  const [inviteAccepted, setInviteAccepted] = useState(false);
  const [invitePreview, setInvitePreview] = useState<{ email: string; displayName: string; role: "Admin" | "Technician" } | null>(null);
  const [invitePreviewError, setInvitePreviewError] = useState("");
  const [invitePassword, setInvitePassword] = useState("");
  const [inviteConfirmPassword, setInviteConfirmPassword] = useState("");
  const [inviteSubmitting, setInviteSubmitting] = useState(false);
  const [inviteError, setInviteError] = useState("");

  // Denetim Logu (Admin) state
  const [auditEntries, setAuditEntries] = useState<AuditLogEntry[]>([]);
  const [auditTotal, setAuditTotal] = useState(0);
  const [auditPage, setAuditPage] = useState(1);
  const [auditLoading, setAuditLoading] = useState(false);
  const auditPageSize = 50;

  // Güvenlik Profilleri (Admin) state
  const emptySecurityProfileForm: SecurityProfileInput = {
    name: "",
    agentDisplayName: "",
    iconBase64: "",
    restrictTrayMenu: false,
    requirePassword: false,
    password: "",
    consentMode: "unattended",
    consentTimeoutSeconds: 30,
    consentDefaultAction: "deny",
    viewOnlyMode: false,
    allowRemoteTerminal: true,
    allowClipboard: true,
    allowFileTransfer: true,
    showConnectionBanner: true
  };
  const [securityProfiles, setSecurityProfiles] = useState<SecurityProfile[]>([]);
  const [editingProfileId, setEditingProfileId] = useState<string | null>(null);
  const [profileForm, setProfileForm] = useState<SecurityProfileInput>(emptySecurityProfileForm);
  const [savingProfile, setSavingProfile] = useState(false);

  // Cihaz Grupları (Admin) state
  const emptyGroupForm: DeviceGroupInput = { name: "", parentGroupId: null, defaultSecurityProfileId: null };
  const [deviceGroups, setDeviceGroups] = useState<DeviceGroup[]>([]);
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null);
  const [groupForm, setGroupForm] = useState<DeviceGroupInput>(emptyGroupForm);
  const [savingGroup, setSavingGroup] = useState(false);
  const [downloadTargetGroupId, setDownloadTargetGroupId] = useState<string>("");

  // Organization Tree View & Quick Inspector State
  const [selectedOrgCompanyId, setSelectedOrgCompanyId] = useState<string | null>(null);
  const [selectedTreeTarget, setSelectedTreeTarget] = useState<{ type: "company" | "dept"; id: string } | null>(null);
  const [expandedTreeNodes, setExpandedTreeNodes] = useState<Set<string>>(new Set());

  const [showNewCompanyModal, setShowNewCompanyModal] = useState(false);
  const [newCompanyName, setNewCompanyName] = useState("");
  const [newCompanyPolicyId, setNewCompanyPolicyId] = useState<string>("");

  const [showNewDeptModal, setShowNewDeptModal] = useState(false);
  const [newDeptCompanyId, setNewDeptCompanyId] = useState<string>("");
  const [newDeptName, setNewDeptName] = useState("");
  const [newDeptPolicyId, setNewDeptPolicyId] = useState<string>("");

  const [showEditGroupModal, setShowEditGroupModal] = useState(false);
  const [editGroupTarget, setEditGroupTarget] = useState<DeviceGroup | null>(null);
  const [editGroupName, setEditGroupName] = useState("");
  const [editGroupPolicyId, setEditGroupPolicyId] = useState<string>("");

  // Quick Device Assignment Modal State
  const [showAssignDevicesModal, setShowAssignDevicesModal] = useState(false);
  const [assignTargetGroup, setAssignTargetGroup] = useState<DeviceGroup | null>(null);
  const [assignSelectedDeviceIds, setAssignSelectedDeviceIds] = useState<Set<string>>(new Set());
  const [deviceAssignSearchQuery, setDeviceAssignSearchQuery] = useState("");
  const [assigningDevices, setAssigningDevices] = useState(false);

  const [showProfileConfigModal, setShowProfileConfigModal] = useState(false);
  const [profileModalTarget, setProfileModalTarget] = useState<{ type: "company" | "dept" | "standalone"; id?: string; name?: string } | null>(null);
  const [orgActiveTab, setOrgActiveTab] = useState<"companies" | "profiles">("companies");
  const [searchOrgQuery, setSearchOrgQuery] = useState("");
  const [searchProfileQuery, setSearchProfileQuery] = useState("");
  const [expandedDeptDevicesId, setExpandedDeptDevicesId] = useState<string | null>(null);

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
      const result = await login(loginEmail.trim(), loginPassword, rememberMe);
      if (result.requiresMfa && result.challengeToken) {
        setMfaChallengeToken(result.challengeToken);
        setMfaCode("");
        setMfaError("");
      } else if (result.token) {
        setStoredAdminToken(result.token, rememberMe);
        setIsAuthenticated(true);
        addActivityLog("Oturum açıldı", "success");
      }
    } catch {
      setAuthError("E-posta veya parola hatalı.");
    } finally {
      setIsLoggingIn(false);
    }
  }

  async function handleVerifyMfa(e: React.FormEvent) {
    e.preventDefault();
    if (!mfaChallengeToken) return;
    setMfaError("");
    setMfaVerifying(true);

    try {
      const result = await verifyMfa(mfaChallengeToken, mfaCode.trim(), rememberMe);
      if (result.token) {
        setStoredAdminToken(result.token, rememberMe);
        setIsAuthenticated(true);
        setMfaChallengeToken(null);
        setMfaCode("");
        addActivityLog("Oturum açıldı (MFA doğrulandı)", "success");
      }
    } catch {
      setMfaError("Kod hatalı veya süresi dolmuş.");
    } finally {
      setMfaVerifying(false);
    }
  }

  function handleCancelMfaChallenge() {
    setMfaChallengeToken(null);
    setMfaCode("");
    setMfaError("");
  }

  function handleLogout() {
    apiLogout();
    clearStoredAdminToken();
    setIsAuthenticated(false);
    setCurrentUser(null);
    setMfaChallengeToken(null);
  }

  async function handleChangePassword(e: React.FormEvent) {
    e.preventDefault();
    if (accountNewPassword !== accountConfirmNewPassword) {
      showToast("Yeni şifreler birbiriyle eşleşmiyor.");
      return;
    }
    setAccountBusy(true);
    try {
      await changePassword(accountCurrentPassword, accountNewPassword);
      setAccountCurrentPassword("");
      setAccountNewPassword("");
      setAccountConfirmNewPassword("");
      showToast("Şifreniz başarıyla güncellendi.");
      addActivityLog("Şifre değiştirildi", "success");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Şifre değiştirilemedi.");
    } finally {
      setAccountBusy(false);
    }
  }

  async function handleStartMfaSetup() {
    setAccountBusy(true);
    try {
      const { secret, provisioningUri } = await setupMfa();
      setMfaSetupSecret(secret);
      setMfaSetupQr(await QRCode.toDataURL(provisioningUri, { width: 220, margin: 1 }));
      setMfaEnableCode("");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "MFA kurulumu başlatılamadı.");
    } finally {
      setAccountBusy(false);
    }
  }

  async function handleEnableMfa(e: React.FormEvent) {
    e.preventDefault();
    setAccountBusy(true);
    try {
      const { recoveryCodes } = await enableMfa(mfaEnableCode.trim());
      setMfaRecoveryCodes(recoveryCodes);
      setMfaSetupQr(null);
      setMfaSetupSecret(null);
      setMfaEnableCode("");
      const me = await getCurrentUser();
      setCurrentUser(me);
      showToast("MFA etkinleştirildi.");
      addActivityLog("MFA etkinleştirildi", "success");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Kod doğrulanamadı.");
    } finally {
      setAccountBusy(false);
    }
  }

  async function handleDisableMfa(e: React.FormEvent) {
    e.preventDefault();
    setAccountBusy(true);
    try {
      await disableMfa(mfaDisablePassword);
      setMfaDisablePassword("");
      const me = await getCurrentUser();
      setCurrentUser(me);
      showToast("MFA kapatıldı.");
      addActivityLog("MFA kapatıldı", "warn");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Şifre hatalı.");
    } finally {
      setAccountBusy(false);
    }
  }

  async function refreshUsers() {
    try {
      setUsers(await listUsers());
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Kullanıcı listesi alınamadı.");
    }
  }

  async function handleCreateUser(e: React.FormEvent) {
    e.preventDefault();
    setCreatingUser(true);
    setCreatedUserCredentials(null);
    setInvitedEmail(null);
    try {
      if (newUserMode === "invite") {
        const result = await inviteUser(newUserEmail.trim(), newUserDisplayName.trim(), newUserRole);
        setInvitedEmail(result.email);
        addActivityLog(`Davet gönderildi: ${result.email}`, "success");
      } else {
        const result = await createUser(newUserEmail.trim(), newUserDisplayName.trim(), newUserRole);
        setCreatedUserCredentials({ email: result.email, temporaryPassword: result.temporaryPassword });
        addActivityLog(`Yeni kullanıcı oluşturuldu: ${result.email}`, "success");
      }
      setNewUserEmail("");
      setNewUserDisplayName("");
      setNewUserRole("Technician");
      await refreshUsers();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "İşlem başarısız oldu.");
    } finally {
      setCreatingUser(false);
    }
  }

  async function handleTestSmtp() {
    if (!smtpTestEmail.trim()) {
      showToast("Test e-postası için bir adres girin.");
      return;
    }
    setTestingSmtp(true);
    try {
      await testSmtp(smtpTestEmail.trim());
      showToast("Test e-postası gönderildi.");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Test e-postası gönderilemedi.");
    } finally {
      setTestingSmtp(false);
    }
  }

  async function handleAcceptInvite(e: React.FormEvent) {
    e.preventDefault();
    if (!inviteToken) return;
    setInviteError("");

    if (invitePassword.length < 8) {
      setInviteError("Şifre en az 8 karakter olmalıdır.");
      return;
    }
    if (invitePassword !== inviteConfirmPassword) {
      setInviteError("Şifreler eşleşmiyor.");
      return;
    }

    setInviteSubmitting(true);
    try {
      const result = await acceptInvite(inviteToken, invitePassword);
      if (result.token) {
        setStoredAdminToken(result.token, true);
        window.history.replaceState(null, "", "/");
        // isAuthenticated zaten true olabilir (aynı tarayıcıda eski bir oturum varsa) — bu durumda state
        // değişmediği için kimlik yenileme effect'i tetiklenmez, o yüzden burada elle tazeliyoruz.
        const me = await getCurrentUser();
        setCurrentUser(me);
        setInviteAccepted(true);
        setIsAuthenticated(true);
      }
    } catch (error) {
      setInviteError(error instanceof Error ? error.message : "Davet kabul edilemedi.");
    } finally {
      setInviteSubmitting(false);
    }
  }

  async function handleSetUserRole(userId: string, role: "Admin" | "Technician") {
    try {
      await setUserRole(userId, role);
      await refreshUsers();
      showToast("Rol güncellendi.");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Rol değiştirilemedi.");
    }
  }

  async function handleToggleUserActive(user: UserSummary) {
    try {
      if (user.isActive) {
        await disableUser(user.id);
        addActivityLog(`Kullanıcı devre dışı bırakıldı: ${user.email}`, "warn");
      } else {
        await enableUser(user.id);
        addActivityLog(`Kullanıcı etkinleştirildi: ${user.email}`, "success");
      }
      await refreshUsers();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "İşlem başarısız oldu.");
    }
  }

  async function handleResetUserMfa(user: UserSummary) {
    try {
      await resetUserMfa(user.id);
      await refreshUsers();
      showToast(`${user.email} için MFA sıfırlandı.`);
    } catch (error) {
      showToast(error instanceof Error ? error.message : "MFA sıfırlanamadı.");
    }
  }

  async function refreshAuditLog(page: number = auditPage) {
    setAuditLoading(true);
    try {
      const result = await getAuditLog(page, auditPageSize);
      setAuditEntries(result.items);
      setAuditTotal(result.total);
      setAuditPage(page);
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Denetim logu alınamadı.");
    } finally {
      setAuditLoading(false);
    }
  }

  async function refreshSecurityProfiles() {
    try {
      setSecurityProfiles(await listSecurityProfiles());
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Güvenlik profilleri alınamadı.");
    }
  }

  function handleEditProfile(profile: SecurityProfile) {
    setEditingProfileId(profile.id);
    setProfileForm({
      name: profile.name,
      agentDisplayName: profile.agentDisplayName ?? "",
      iconBase64: profile.iconBase64 ?? "",
      restrictTrayMenu: profile.restrictTrayMenu,
      requirePassword: profile.requirePassword,
      password: "",
      consentMode: profile.consentMode ?? "unattended",
      consentTimeoutSeconds: profile.consentTimeoutSeconds ?? 30,
      consentDefaultAction: profile.consentDefaultAction ?? "deny",
      viewOnlyMode: profile.viewOnlyMode ?? false,
      allowRemoteTerminal: profile.allowRemoteTerminal ?? true,
      allowClipboard: profile.allowClipboard ?? true,
      allowFileTransfer: profile.allowFileTransfer ?? true,
      showConnectionBanner: profile.showConnectionBanner ?? true
    });
  }

  function handleCancelEditProfile() {
    setEditingProfileId(null);
    setProfileForm(emptySecurityProfileForm);
  }

  function handleIconFileChange(file: File | null) {
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      const result = reader.result as string;
      // "data:image/png;base64,XXXX" -> sadece base64 kısmını sakla
      const base64 = result.split(",")[1] ?? "";
      setProfileForm((prev) => ({ ...prev, iconBase64: base64 }));
    };
    reader.readAsDataURL(file);
  }

  async function handleSaveProfile(e: React.FormEvent) {
    e.preventDefault();
    setSavingProfile(true);
    try {
      if (editingProfileId) {
        await updateSecurityProfile(editingProfileId, profileForm);
        addActivityLog(`Güvenlik profili güncellendi: ${profileForm.name}`, "success");
      } else {
        await createSecurityProfile(profileForm);
        addActivityLog(`Güvenlik profili oluşturuldu: ${profileForm.name}`, "success");
      }
      handleCancelEditProfile();
      await refreshSecurityProfiles();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Profil kaydedilemedi.");
    } finally {
      setSavingProfile(false);
    }
  }

  async function handleDeleteProfile(profile: SecurityProfile) {
    try {
      await deleteSecurityProfile(profile.id);
      if (editingProfileId === profile.id) {
        handleCancelEditProfile();
      }
      await refreshSecurityProfiles();
      addActivityLog(`Güvenlik profili silindi: ${profile.name}`, "warn");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Profil silinemedi.");
    }
  }

  async function handleAssignSecurityProfile(deviceId: string, securityProfileId: string) {
    try {
      await assignSecurityProfile(deviceId, securityProfileId || null);
      setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, securityProfileId: securityProfileId || null } : d)));
      showToast("Güvenlik profili güncellendi.");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Güvenlik profili atanamadı.");
    }
  }

  async function refreshActiveAlerts() {
    try {
      setActiveAlerts(await getActiveAlerts());
    } catch {
      // sessizce geç — uyarı rozetleri opsiyonel, ana akışı bozmasın
    }
  }

  async function refreshDeviceGroups() {
    try {
      setDeviceGroups(await listDeviceGroups());
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Cihaz grupları alınamadı.");
    }
  }

  function handleEditGroup(group: DeviceGroup) {
    setEditingGroupId(group.id);
    setGroupForm({
      name: group.name,
      parentGroupId: group.parentGroupId ?? null,
      defaultSecurityProfileId: group.defaultSecurityProfileId ?? null
    });
  }

  function handleCancelEditGroup() {
    setEditingGroupId(null);
    setGroupForm(emptyGroupForm);
  }

  async function handleSaveGroup(e: React.FormEvent) {
    e.preventDefault();
    setSavingGroup(true);
    try {
      if (editingGroupId) {
        await updateDeviceGroup(editingGroupId, groupForm);
        addActivityLog(`Grup güncellendi: ${groupForm.name}`, "success");
      } else {
        await createDeviceGroup(groupForm);
        addActivityLog(`Grup oluşturuldu: ${groupForm.name}`, "success");
      }
      handleCancelEditGroup();
      await refreshDeviceGroups();
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Grup kaydedilemedi.");
    } finally {
      setSavingGroup(false);
    }
  }

  async function handleDeleteGroup(group: DeviceGroup) {
    try {
      await deleteDeviceGroup(group.id);
      if (editingGroupId === group.id) {
        handleCancelEditGroup();
      }
      await refreshDeviceGroups();
      addActivityLog(`Grup silindi: ${group.name}`, "warn");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Grup silinemedi.");
    }
  }

  async function handleAssignDeviceGroup(deviceId: string, groupId: string) {
    try {
      await assignDeviceGroup(deviceId, groupId || null);
      setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, groupId: groupId || null } : d)));
      showToast("Grup güncellendi.");
    } catch (error) {
      showToast(error instanceof Error ? error.message : "Grup atanamadı.");
    }
  }


  function toggleTreeNode(id: string) {
    setExpandedTreeNodes((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  }

  function expandAllTreeNodes() {
    setExpandedTreeNodes(new Set(deviceGroups.map((g) => g.id)));
  }

  function collapseAllTreeNodes() {
    setExpandedTreeNodes(new Set());
  }

  async function handleCreateCompany(e: React.FormEvent) {
    e.preventDefault();
    if (!newCompanyName.trim()) return;
    setSavingGroup(true);
    try {
      const created = await createDeviceGroup({
        name: newCompanyName.trim(),
        parentGroupId: null,
        defaultSecurityProfileId: newCompanyPolicyId || null
      });
      setShowNewCompanyModal(false);
      setNewCompanyName("");
      setNewCompanyPolicyId("");
      setSelectedOrgCompanyId(created.id);
      setSelectedTreeTarget({ type: "company", id: created.id });
      setExpandedTreeNodes((prev) => new Set([...prev, created.id]));
      await refreshDeviceGroups();
      showToast(`"${created.name}" şirketi oluşturuldu.`);
      addActivityLog(`Şirket oluşturuldu: ${created.name}`, "success");
    } catch (err: any) {
      showToast(err?.message || "Şirket oluşturulamadı.");
    } finally {
      setSavingGroup(false);
    }
  }

  async function handleCreateDepartment(e: React.FormEvent) {
    e.preventDefault();
    const parentId = newDeptCompanyId || selectedOrgCompany?.id;
    if (!parentId || !newDeptName.trim()) {
      showToast("Lütfen bir üst şirket ve departman adı girin.");
      return;
    }
    const parentComp = deviceGroups.find((g) => g.id === parentId);
    setSavingGroup(true);
    try {
      const created = await createDeviceGroup({
        name: newDeptName.trim(),
        parentGroupId: parentId,
        defaultSecurityProfileId: newDeptPolicyId || null
      });
      setShowNewDeptModal(false);
      setNewDeptName("");
      setNewDeptPolicyId("");
      setNewDeptCompanyId("");
      await refreshDeviceGroups();
      setExpandedTreeNodes((prev) => new Set([...prev, parentId, created.id]));
      setSelectedTreeTarget({ type: "dept", id: created.id });
      showToast(`"${created.name}" departmanı oluşturuldu.`);
      addActivityLog(`Departman oluşturuldu: ${created.name} (${parentComp?.name || "Şirket"})`, "success");
    } catch (err: any) {
      showToast(err?.message || "Departman oluşturulamadı.");
    } finally {
      setSavingGroup(false);
    }
  }

  async function handleQuickUpdateGroupPolicy(group: DeviceGroup, policyId: string | null) {
    try {
      await updateDeviceGroup(group.id, {
        name: group.name,
        parentGroupId: group.parentGroupId ?? null,
        defaultSecurityProfileId: policyId || null
      });
      setDeviceGroups((prev) => prev.map((g) => (g.id === group.id ? { ...g, defaultSecurityProfileId: policyId || null } : g)));
      showToast(`"${group.name}" güvenlik politikası güncellendi.`);
      addActivityLog(`Grup politikası güncellendi: ${group.name}`, "info");
    } catch (err: any) {
      showToast(err?.message || "Politika atanamadı.");
    }
  }

  async function handleApplyPolicyPreset(
    targetGroup: DeviceGroup,
    presetType: "unattended" | "always_prompt" | "prompt_if_active" | "view_only" | "inherit"
  ) {
    if (presetType === "inherit") {
      await handleQuickUpdateGroupPolicy(targetGroup, null);
      return;
    }

    // Check if an existing profile exactly matches this preset
    const matched = securityProfiles.find((p) => {
      if (presetType === "unattended") {
        return p.consentMode === "unattended" && !p.viewOnlyMode && p.allowRemoteTerminal && p.allowClipboard && p.allowFileTransfer;
      }
      if (presetType === "always_prompt") {
        return p.consentMode === "always_prompt" && !p.viewOnlyMode && p.allowRemoteTerminal && p.allowClipboard && p.allowFileTransfer;
      }
      if (presetType === "prompt_if_active") {
        return p.consentMode === "prompt_if_active" && !p.viewOnlyMode && p.allowRemoteTerminal && p.allowClipboard && p.allowFileTransfer;
      }
      if (presetType === "view_only") {
        return p.viewOnlyMode;
      }
      return false;
    });

    if (matched) {
      await handleQuickUpdateGroupPolicy(targetGroup, matched.id);
    } else {
      // Auto-create standard preset profile
      try {
        setSavingProfile(true);
        const presetName =
          presetType === "unattended"
            ? "Doğrudan Erişim (Unattended)"
            : presetType === "always_prompt"
            ? "Kullanıcı Onaylı Erişim (30s)"
            : presetType === "prompt_if_active"
            ? "Akıllı Onay (Aktifken Sor)"
            : "Sadece İzleme Modu";

        const created = await createSecurityProfile({
          name: `${targetGroup.name} - ${presetName}`,
          agentDisplayName: "NexMote Agent",
          consentMode:
            presetType === "always_prompt"
              ? "always_prompt"
              : presetType === "prompt_if_active"
              ? "prompt_if_active"
              : "unattended",
          consentTimeoutSeconds: 30,
          consentDefaultAction: "deny",
          viewOnlyMode: presetType === "view_only",
          allowRemoteTerminal: presetType !== "view_only",
          allowClipboard: true,
          allowFileTransfer: presetType !== "view_only",
          showConnectionBanner: true,
          restrictTrayMenu: false,
          requirePassword: false
        });
        await refreshSecurityProfiles();
        await handleQuickUpdateGroupPolicy(targetGroup, created.id);
      } catch (err: any) {
        showToast(err?.message || "Politika uygulanamadı.");
      } finally {
        setSavingProfile(false);
      }
    }
  }

  function handleOpenAssignDevices(group: DeviceGroup) {
    setAssignTargetGroup(group);
    const existingGroupDeviceIds = new Set(devices.filter((d) => d.groupId === group.id).map((d) => d.id));
    setAssignSelectedDeviceIds(existingGroupDeviceIds);
    setDeviceAssignSearchQuery("");
    setShowAssignDevicesModal(true);
  }

  async function handleSaveDeviceAssignments() {
    if (!assignTargetGroup) return;
    setAssigningDevices(true);
    try {
      const toAdd = devices.filter((d) => assignSelectedDeviceIds.has(d.id) && d.groupId !== assignTargetGroup.id);
      const toRemove = devices.filter((d) => !assignSelectedDeviceIds.has(d.id) && d.groupId === assignTargetGroup.id);

      for (const dev of toAdd) {
        await assignDeviceGroup(dev.id, assignTargetGroup.id);
      }
      for (const dev of toRemove) {
        await assignDeviceGroup(dev.id, null);
      }

      setDevices((prev) =>
        prev.map((d) => {
          if (assignSelectedDeviceIds.has(d.id)) {
            return { ...d, groupId: assignTargetGroup.id };
          } else if (d.groupId === assignTargetGroup.id) {
            return { ...d, groupId: null };
          }
          return d;
        })
      );

      setShowAssignDevicesModal(false);
      showToast(`"${assignTargetGroup.name}" cihazları güncellendi (${assignSelectedDeviceIds.size} cihaz).`);
      addActivityLog(`Grup cihazları güncellendi: ${assignTargetGroup.name}`, "success");
      await refresh(false);
    } catch (err: any) {
      showToast(err?.message || "Cihazlar atanamadı.");
    } finally {
      setAssigningDevices(false);
    }
  }

  async function handleUnassignDevice(deviceId: string, groupName: string) {
    try {
      await assignDeviceGroup(deviceId, null);
      setDevices((prev) => prev.map((d) => (d.id === deviceId ? { ...d, groupId: null } : d)));
      showToast(`Cihaz "${groupName}" grubundan çıkarıldı.`);
      addActivityLog(`Cihaz gruptan çıkarıldı: ${groupName}`, "info");
    } catch (err: any) {
      showToast(err?.message || "Cihaz gruptan çıkarılamadı.");
    }
  }

  function handleOpenEditGroup(group: DeviceGroup) {
    setEditGroupTarget(group);
    setEditGroupName(group.name);
    setEditGroupPolicyId(group.defaultSecurityProfileId ?? "");
    setShowEditGroupModal(true);
  }

  async function handleSaveEditGroup(e: React.FormEvent) {
    e.preventDefault();
    if (!editGroupTarget || !editGroupName.trim()) return;
    setSavingGroup(true);
    try {
      const updated = await updateDeviceGroup(editGroupTarget.id, {
        name: editGroupName.trim(),
        parentGroupId: editGroupTarget.parentGroupId ?? null,
        defaultSecurityProfileId: editGroupPolicyId || null
      });
      setDeviceGroups((prev) => prev.map((g) => (g.id === editGroupTarget.id ? updated : g)));
      setShowEditGroupModal(false);
      setEditGroupTarget(null);
      showToast(`"${updated.name}" güncellendi.`);
      addActivityLog(`Grup güncellendi: ${updated.name}`, "success");
    } catch (err: any) {
      showToast(err?.message || "Grup güncellenemedi.");
    } finally {
      setSavingGroup(false);
    }
  }

  function openProfileEditorFor(profileId?: string | null, target?: { type: "company" | "dept" | "standalone"; id?: string; name?: string }) {
    if (profileId) {
      const p = securityProfiles.find((x) => x.id === profileId);
      if (p) {
        handleEditProfile(p);
      } else {
        handleCancelEditProfile();
      }
    } else {
      handleCancelEditProfile();
    }
    setProfileModalTarget(target || null);
    setShowProfileConfigModal(true);
  }

  async function handleSaveProfileModal(e: React.FormEvent) {
    e.preventDefault();
    if (profileForm.requirePassword && !editingProfileId && (!profileForm.password || profileForm.password.length < 6)) {
      showToast("Yönetici parolası en az 6 karakter olmalıdır.");
      return;
    }
    setSavingProfile(true);
    try {
      let savedProfileId = editingProfileId;
      if (editingProfileId) {
        await updateSecurityProfile(editingProfileId, profileForm);
        showToast("Güvenlik profili güncellendi.");
        addActivityLog(`Güvenlik profili güncellendi: ${profileForm.name}`, "success");
      } else {
        const created = await createSecurityProfile(profileForm);
        savedProfileId = created.id;
        showToast("Yeni güvenlik profili oluşturuldu.");
        addActivityLog(`Güvenlik profili oluşturuldu: ${profileForm.name}`, "success");
      }
      await refreshSecurityProfiles();
      if (profileModalTarget && profileModalTarget.id && savedProfileId) {
        const grp = deviceGroups.find((g) => g.id === profileModalTarget.id);
        if (grp) {
          await handleQuickUpdateGroupPolicy(grp, savedProfileId);
        }
      }
      setShowProfileConfigModal(false);
      handleCancelEditProfile();
    } catch (err: any) {
      showToast(err?.message || "Profil kaydedilemedi.");
    } finally {
      setSavingProfile(false);
    }
  }

  function addActivityLog(text: string, level: "info" | "success" | "warn" = "info") {
    const time = new Date().toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit", second: "2-digit" });
    setActivityLogs(prev => [{ id: Math.random().toString(36).substring(2, 9), text, time, level }, ...prev.slice(0, 49)]);
  }

  function getDeviceEffectiveProfile(dev?: DeviceSummary | null): SecurityProfile | null {
    if (!dev) return null;
    if (dev.securityProfileId) {
      return securityProfiles.find((p) => p.id === dev.securityProfileId) || null;
    }
    if (dev.groupId) {
      const grp = deviceGroups.find((g) => g.id === dev.groupId);
      if (grp?.defaultSecurityProfileId) {
        return securityProfiles.find((p) => p.id === grp.defaultSecurityProfileId) || null;
      }
      if (grp?.parentGroupId) {
        const parent = deviceGroups.find((g) => g.id === grp.parentGroupId);
        if (parent?.defaultSecurityProfileId) {
          return securityProfiles.find((p) => p.id === parent.defaultSecurityProfileId) || null;
        }
      }
    }
    return null;
  }

  function getDeviceGroupLabel(groupId?: string | null): string {
    if (!groupId) return "Atanmamış";
    const grp = deviceGroups.find((g) => g.id === groupId);
    if (!grp) return "Bilinmeyen";
    if (grp.parentGroupId) {
      const parent = deviceGroups.find((g) => g.id === grp.parentGroupId);
      return parent ? `${parent.name} > ${grp.name}` : grp.name;
    }
    return grp.name;
  }

  function getProfileUsageStats(profileId: string) {
    const directCompanies = rootCompanies.filter(c => c.defaultSecurityProfileId === profileId).length;
    const directDepts = deviceGroups.filter(g => g.parentGroupId && g.defaultSecurityProfileId === profileId).length;
    const directDevices = devices.filter(d => d.securityProfileId === profileId).length;
    
    let totalImpactedDevices = 0;
    devices.forEach(d => {
      const eff = getDeviceEffectiveProfile(d);
      if (eff?.id === profileId) {
        totalImpactedDevices++;
      }
    });

    return { directCompanies, directDepts, directDevices, totalImpactedDevices };
  }

  function handleCloneProfile(profile: SecurityProfile) {
    setEditingProfileId(null);
    setProfileForm({
      name: `${profile.name} (Kopya)`,
      agentDisplayName: profile.agentDisplayName ?? "NexMote Agent",
      iconBase64: profile.iconBase64 ?? undefined,
      consentMode: profile.consentMode,
      consentTimeoutSeconds: profile.consentTimeoutSeconds,
      consentDefaultAction: profile.consentDefaultAction,
      viewOnlyMode: profile.viewOnlyMode,
      allowRemoteTerminal: profile.allowRemoteTerminal,
      allowClipboard: profile.allowClipboard,
      allowFileTransfer: profile.allowFileTransfer,
      showConnectionBanner: profile.showConnectionBanner,
      restrictTrayMenu: profile.restrictTrayMenu,
      requirePassword: profile.requirePassword,
      password: ""
    });
    setProfileModalTarget({ type: "standalone" });
    setShowProfileConfigModal(true);
    showToast(`"${profile.name}" şablon olarak alındı. Düzenleyip kaydedin.`);
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
    if (!inviteToken) return;
    getInvitePreview(inviteToken)
      .then(setInvitePreview)
      .catch((error) => setInvitePreviewError(error instanceof Error ? error.message : "Davet geçersiz veya süresi dolmuş."));
  }, [inviteToken]);

  useEffect(() => {
    if (!isAuthenticated) return;
    let cancelled = false;

    (async () => {
      try {
        const me = await getCurrentUser();
        if (cancelled) return;
        setCurrentUser(me);

        refresh();
        refreshDownloads();
        refreshLatestVersion();
        refreshServerMetrics();
        refreshActiveAlerts();
        if (me.role === "Admin") {
          refreshSettings();
          refreshSecurityProfiles();
          refreshDeviceGroups();
        }
      } catch {
        if (!cancelled) handleLogout();
      }
    })();

    const interval = setInterval(() => {
      refresh(false);
      refreshActiveAlerts();
      if (view === "settings") {
        refreshServerMetrics(false);
      }
    }, 3000);
    return () => {
      cancelled = true;
      clearInterval(interval);
    };
  }, [isAuthenticated, view]);

  useEffect(() => {
    if (!isAuthenticated || currentUser?.role !== "Admin") return;
    if (view === "users") refreshUsers();
    if (view === "audit-log") refreshAuditLog(1);
    if (view === "security-profiles") refreshSecurityProfiles();
    if (view === "device-groups") refreshDeviceGroups();
  }, [isAuthenticated, currentUser?.role, view]);

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

  const activeAlertDeviceIds = useMemo(() => new Set(activeAlerts.map((a) => a.deviceId)), [activeAlerts]);

  const matchingGroupIds = useMemo(() => {
    if (selectedGroupId === "all") return null;
    const ids = new Set<string>([selectedGroupId]);
    let changed = true;
    while (changed) {
      changed = false;
      for (const g of deviceGroups) {
        if (g.parentGroupId && ids.has(g.parentGroupId) && !ids.has(g.id)) {
          ids.add(g.id);
          changed = true;
        }
      }
    }
    return ids;
  }, [selectedGroupId, deviceGroups]);

  const filteredAndSortedDevices = useMemo(() => {
    const q = query.trim().toLowerCase();
    const result = devices.filter((d) => {
      if (matchingGroupIds && (!d.groupId || !matchingGroupIds.has(d.groupId))) {
        return false;
      }

      const matchesQuery =
        !q ||
        d.deviceName.toLowerCase().includes(q) ||
        (d.ipAddress || "").toLowerCase().includes(q) ||
        (d.operatingSystem || "").toLowerCase().includes(q) ||
        (d.locationCode || "").toLowerCase().includes(q) ||
        (d.activeUser || "").toLowerCase().includes(q);

      const isWarning = Boolean(
        isVersionOlder(d.agentVersion, latestAgentVersion) ||
        activeAlertDeviceIds.has(d.id)
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
  }, [devices, query, statusFilter, sortField, sortDirection, latestAgentVersion, activeAlertDeviceIds, matchingGroupIds]);

  const selectedDevice = useMemo(
    () => devices.find((d) => d.id === selectedDeviceId) ?? devices[0] ?? null,
    [devices, selectedDeviceId]
  );

  const deviceActiveAlerts = useMemo(
    () => (selectedDevice ? activeAlerts.filter((a) => a.deviceId === selectedDevice.id) : []),
    [activeAlerts, selectedDevice]
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

  const rootCompanies = useMemo(() => {
    return deviceGroups.filter((g) => !g.parentGroupId);
  }, [deviceGroups]);

  const selectedOrgCompany = useMemo(() => {
    if (selectedOrgCompanyId) {
      const found = rootCompanies.find((c) => c.id === selectedOrgCompanyId);
      if (found) return found;
    }
    return rootCompanies[0] || null;
  }, [rootCompanies, selectedOrgCompanyId]);

  const selectedCompanyDepartments = useMemo(() => {
    if (!selectedOrgCompany) return [];
    return deviceGroups.filter((g) => g.parentGroupId === selectedOrgCompany.id);
  }, [deviceGroups, selectedOrgCompany]);

  function getCompanyDeviceCount(companyId: string) {
    const deptIds = new Set(deviceGroups.filter(g => g.parentGroupId === companyId).map(g => g.id));
    deptIds.add(companyId);
    return devices.filter(d => d.groupId && deptIds.has(d.groupId)).length;
  }

  function getDepartmentDeviceCount(deptId: string) {
    return devices.filter(d => d.groupId === deptId).length;
  }

  const userInitial = (currentUser?.displayName || currentUser?.email || "N").charAt(0).toUpperCase();
  const userDisplayName = currentUser?.displayName || currentUser?.email?.split("@")[0] || "Kullanıcı";
  const roleLabel = currentUser?.role === "Admin" ? "Yönetici" : "Teknisyen";
  const onlineCount = devices.filter((d) => d.isOnline).length;
  const warningCount = devices.filter(
    (d) => isVersionOlder(d.agentVersion, latestAgentVersion) || activeAlertDeviceIds.has(d.id)
  ).length;

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

  // --- DAVET KABUL EKRANI (public, /invite/{token}) ---
  if (inviteToken && !inviteAccepted) {
    return (
      <InviteAcceptScreen
        invitePreview={invitePreview}
        invitePreviewError={invitePreviewError}
        inviteError={inviteError}
        invitePassword={invitePassword}
        setInvitePassword={setInvitePassword}
        inviteConfirmPassword={inviteConfirmPassword}
        setInviteConfirmPassword={setInviteConfirmPassword}
        inviteSubmitting={inviteSubmitting}
        handleAcceptInvite={handleAcceptInvite}
      />
    );
  }

  // --- LOGIN VIEW ---
  if (!isAuthenticated) {
    return (
      <LoginScreen
        settings={settings}
        authError={authError}
        isLoggingIn={isLoggingIn}
        loginEmail={loginEmail}
        setLoginEmail={setLoginEmail}
        loginPassword={loginPassword}
        setLoginPassword={setLoginPassword}
        showLoginPassword={showLoginPassword}
        setShowLoginPassword={setShowLoginPassword}
        rememberMe={rememberMe}
        setRememberMe={setRememberMe}
        handleLogin={handleLogin}
        mfaChallengeToken={mfaChallengeToken}
        mfaCode={mfaCode}
        setMfaCode={setMfaCode}
        mfaError={mfaError}
        mfaVerifying={mfaVerifying}
        handleVerifyMfa={handleVerifyMfa}
        handleCancelMfaChallenge={handleCancelMfaChallenge}
      />
    );
  }

  // --- MAIN AUTHENTICATED APP ---
  return (
    <div className="app-layout">
      {/* 1. Modern Categorized Sidebar */}
      <AppSidebar
        sidebarCollapsed={sidebarCollapsed}
        toggleSidebar={toggleSidebar}
        view={view}
        setView={setView}
        currentUser={currentUser}
        onlineCount={onlineCount}
        devices={devices}
        rootCompanies={rootCompanies}
        users={users}
        userInitial={userInitial}
        userDisplayName={userDisplayName}
        roleLabel={roleLabel}
        handleLogout={handleLogout}
      />

      {/* 2. App Main Content */}
      <div className="app-main">
        {/* Top Header */}
        <AppHeader
          devices={devices}
          onlineCount={onlineCount}
          warningCount={warningCount}
          query={query}
          setQuery={setQuery}
          loading={loading}
          refresh={refresh}
          showNotifications={showNotifications}
          setShowNotifications={setShowNotifications}
          activityLogs={activityLogs}
          setActivityLogs={setActivityLogs}
          userInitial={userInitial}
          userDisplayName={userDisplayName}
          roleLabel={roleLabel}
          currentUser={currentUser}
          handleLogout={handleLogout}
        />

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

              {deviceGroups.length > 0 && (
                <div className="filter-group" style={{ gap: "6px" }}>
                  <Building2 size={13} style={{ color: "var(--text-dim)" }} />
                  <select
                    className="form-input"
                    style={{ height: 26, fontSize: 11.5, padding: "0 8px", width: "auto", minWidth: 170 }}
                    value={selectedGroupId}
                    onChange={(e) => setSelectedGroupId(e.target.value)}
                    title="Şirket / Departmana göre filtrele"
                  >
                    <option value="all">Tüm Organizasyonlar ({devices.length})</option>
                    {rootCompanies.map((comp) => {
                      const depts = deviceGroups.filter((g) => g.parentGroupId === comp.id);
                      return (
                        <optgroup key={comp.id} label={`🏢 ${comp.name}`}>
                          <option value={comp.id}>{comp.name} (Tümü)</option>
                          {depts.map((d) => (
                            <option key={d.id} value={d.id}>📁 {d.name}</option>
                          ))}
                        </optgroup>
                      );
                    })}
                  </select>
                </div>
              )}
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
                          <td colSpan={9} className="empty-table-cell" style={{ padding: "32px 16px", textAlign: "center" }}>
                            {selectedGroupId !== "all" ? (
                              <div style={{ display: "flex", flexDirection: "column", alignItems: "center", gap: "8px" }}>
                                <Building2 size={24} style={{ color: "var(--primary)" }} />
                                <div style={{ fontSize: "13px", fontWeight: 600, color: "var(--text-main)" }}>
                                  Seçilen Şirket/Departmana Ait Henüz Cihaz Atanmadı
                                </div>
                                <div style={{ fontSize: "11.5px", color: "var(--text-dim)", maxWidth: 380 }}>
                                  Cihazları bu departmana atamak için Cihaz Listesinden ilgili bilgisayarın detayına giderek Şirket &amp; Departman seçimi yapabilirsiniz.
                                </div>
                              </div>
                            ) : (
                              "Kriterlere uygun cihaz bulunamadı."
                            )}
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
                  {deviceActiveAlerts.length > 0 && (
                    <div className="stale-data-notice" style={{ gridColumn: "1 / -1" }}>
                      <AlertCircle size={15} />
                      <span>
                        {deviceActiveAlerts.map((a) => describeAlertType(a.alertType)).join(" · ")}
                        {" — "}
                        {formatLastSeen(deviceActiveAlerts[0].triggeredAt)} tetiklendi.
                      </span>
                    </div>
                  )}

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
                      {currentUser?.role === "Admin" && (
                        <div className="bento-spec-item" style={{ gridColumn: "1 / -1", background: "var(--bg-hover)", padding: "10px 14px", borderRadius: "8px", border: "1px solid var(--border-subtle)" }}>
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "6px" }}>
                            <span className="bento-spec-label" style={{ fontWeight: 600, color: "var(--text-main)" }}>🏢 Şirket &amp; Departman Ataması</span>
                            {selectedDevice.groupId && (
                              <span style={{ fontSize: "11px", color: "var(--text-dim)" }}>
                                {getDeviceGroupLabel(selectedDevice.groupId)}
                              </span>
                            )}
                          </div>
                          
                          <select
                            className="form-input"
                            style={{ height: 32, fontSize: "12.5px" }}
                            value={selectedDevice.groupId ?? ""}
                            onChange={(e) => handleAssignDeviceGroup(selectedDevice.id, e.target.value)}
                          >
                            <option value="">— Atanmamış (Standart Erişim)</option>
                            {rootCompanies.map((comp) => {
                              const depts = deviceGroups.filter((g) => g.parentGroupId === comp.id);
                              return (
                                <optgroup key={comp.id} label={`🏢 ${comp.name}`}>
                                  <option value={comp.id}>{comp.name} (Şirket Geneli)</option>
                                  {depts.map((d) => (
                                    <option key={d.id} value={d.id}>📁 {comp.name} &gt; {d.name}</option>
                                  ))}
                                </optgroup>
                              );
                            })}
                          </select>

                          {/* Canlı Güvenlik Politikası Rozeti */}
                          {(() => {
                            const eff = getDeviceEffectiveProfile(selectedDevice);
                            return (
                              <div style={{ display: "flex", alignItems: "center", gap: "8px", marginTop: "8px", flexWrap: "wrap" }}>
                                <span style={{ fontSize: "11.5px", color: "var(--text-dim)" }}>Uygulanan Politika:</span>
                                {eff ? (
                                  <span className="shield-tag" style={{ fontSize: "11px" }}>
                                    🛡️ {eff.name}{" "}
                                    {eff.consentMode === "always_prompt"
                                      ? "(🟡 Her Zaman Onay)"
                                      : eff.consentMode === "prompt_if_active"
                                      ? "(🔵 Aktifken Onay)"
                                      : "(🟢 Doğrudan Erişim)"}
                                    {eff.requirePassword && " · 🔒 Şifreli"}
                                    {eff.viewOnlyMode ? " · 👁️ Sadece İzle" : " · ⚡ Tam Kontrol"}
                                  </span>
                                ) : (
                                  <span className="shield-tag" style={{ fontSize: "11px", background: "rgba(100, 116, 139, 0.12)", color: "var(--text-dim)" }}>
                                    🛡️ Standart Erişim (Kısıtlama Yok)
                                  </span>
                                )}
                              </div>
                            );
                          })()}
                        </div>
                      )}
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
        {view === "downloads" && <DownloadsView downloads={downloads} />}

        {/* View 3: Server Settings */}
        {view === "settings" && (
          <div className="content-pane">
            <div className="settings-tabs-bar">
              {currentUser?.role === "Admin" && (
                <>
                  <button
                    type="button"
                    className={`settings-tab-btn ${settingsActiveTab === "server" ? "active" : ""}`}
                    onClick={() => setSettingsActiveTab("server")}
                  >
                    <Server size={15} /> Sunucu &amp; Metrikler
                  </button>
                  <button
                    type="button"
                    className={`settings-tab-btn ${settingsActiveTab === "alerts" ? "active" : ""}`}
                    onClick={() => setSettingsActiveTab("alerts")}
                  >
                    <Bell size={15} /> E-Posta (SMTP) &amp; Uyarılar
                  </button>
                </>
              )}
              <button
                type="button"
                className={`settings-tab-btn ${settingsActiveTab === "account" ? "active" : ""}`}
                onClick={() => setSettingsActiveTab("account")}
              >
                <User size={15} /> Hesabım &amp; Güvenlik (MFA)
              </button>
            </div>

            {/* TAB 1: SUNUCU & METRİKLER */}
            {settingsActiveTab === "server" && currentUser?.role === "Admin" && (
              <>
                <div className="content-card">
                  <h2 className="content-card-title">Sunucu ve Kayıt Yapılandırması</h2>
                  <p className="content-card-copy">
                    Ajan cihazlarının sunucuya kayıt olması ve canlılık sinyali ayarları.
                  </p>

                  <form onSubmit={handleSaveSettings} className="settings-form">
                    <div className="form-group">
                      <label className="form-label">Sunucu Adresi</label>
                      <input
                        type="url"
                        className="form-input"
                        value={settings.serverUrl}
                        onChange={(e) => setSettings({ ...settings, serverUrl: e.target.value })}
                        required
                      />
                      <span className="form-help">Ajan ve teknisyen uygulamalarının bağlandığı güvenli sunucu URL'i.</span>
                    </div>

                    <div className="stale-data-notice" style={{ background: "rgba(16, 185, 129, 0.08)", borderColor: "rgba(16, 185, 129, 0.25)", margin: "var(--space-3) 0" }}>
                      <ShieldCheck size={18} style={{ color: "#10b981", flexShrink: 0 }} />
                      <div style={{ fontSize: "12.5px", color: "var(--text-main)", lineHeight: 1.5 }}>
                        <strong>🔒 Sıfır-Kodlu Otomatik Kayıt (Zero-Touch):</strong> İstemciler kurulum sonrasında sunucuya doğrudan ve güvenli kriptografik el sıkışma ile kaydolur. Manuel anahtar girilmesine gerek yoktur. Şirket veya departmana özel otomatik atamalar için <em>Şirketler &amp; Güvenlik</em> sekmesindeki provizyon scriptlerini kullanabilirsiniz.
                      </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="form-label">Canlılık Sinyali Sıklığı (sn)</label>
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

                {/* Sunucu Canlı Metrikleri */}
                {serverMetrics && (
                  <div className="content-card">
                    <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "var(--space-2)" }}>
                      <h2 className="content-card-title">Sunucu Canlı Performansı</h2>
                      <button
                        type="button"
                        className="btn-secondary"
                        style={{ height: 28, fontSize: 12, padding: "0 10px" }}
                        onClick={() => refreshServerMetrics(true)}
                        disabled={metricsLoading}
                      >
                        <RefreshCw size={13} className={metricsLoading ? "animate-spin" : ""} /> Yenile
                      </button>
                    </div>
                    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fit, minmax(180px, 1fr))", gap: "var(--space-3)" }}>
                      <div className="metric-box">
                        <div className="metric-label">CPU Kullanımı</div>
                        <div className="metric-value">{serverMetrics.cpuUsagePercent.toFixed(1)}%</div>
                        {renderSparkline(cpuHistory, "#2563eb", 100)}
                      </div>
                      <div className="metric-box">
                        <div className="metric-label">RAM Kullanımı</div>
                        <div className="metric-value">{serverMetrics.memoryUsedMb} / {serverMetrics.memoryTotalMb} MB</div>
                        <div className="progress-bar-bg" style={{ marginTop: 8 }}>
                          <div
                            className="progress-bar-fill"
                            style={{
                              width: `${Math.min(100, (serverMetrics.memoryUsedMb / (serverMetrics.memoryTotalMb || 1)) * 100)}%`,
                              background: "#3b82f6"
                            }}
                          />
                        </div>
                      </div>
                      <div className="metric-box">
                        <div className="metric-label">Disk Boş Alan</div>
                        <div className="metric-value">{serverMetrics.diskFreeGb.toFixed(1)} GB</div>
                        <div className="metric-subtext">Çalışma Süresi: {formatUptime(serverMetrics.uptimeSeconds)}</div>
                      </div>
                    </div>
                  </div>
                )}
              </>
            )}

            {/* TAB 2: E-POSTA & UYARILAR */}
            {settingsActiveTab === "alerts" && currentUser?.role === "Admin" && (
              <>
                <div className="content-card">
                  <h2 className="content-card-title">E-posta (SMTP) Yapılandırması</h2>
                  <p className="content-card-copy">
                    Kullanıcı davet e-postaları ve sistem uyarı bildirimleri bu SMTP sunucusu üzerinden gönderilir.
                  </p>

                  <form onSubmit={handleSaveSettings} className="settings-form">
                    <div style={{ display: "grid", gridTemplateColumns: "2fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="form-label">SMTP Sunucu Adresi</label>
                        <input
                          type="text"
                          className="form-input"
                          placeholder="smtp.hostinger.com"
                          value={settings.smtpHost ?? ""}
                          onChange={(e) => setSettings({ ...settings, smtpHost: e.target.value })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="form-label">Port</label>
                        <input
                          type="number"
                          className="form-input"
                          value={settings.smtpPort ?? 465}
                          onChange={(e) => setSettings({ ...settings, smtpPort: Number(e.target.value) })}
                        />
                      </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="form-label">Kullanıcı Adı</label>
                        <input
                          type="text"
                          className="form-input"
                          placeholder="admin@nexmote.com"
                          value={settings.smtpUsername ?? ""}
                          onChange={(e) => setSettings({ ...settings, smtpUsername: e.target.value })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="form-label">Şifre</label>
                        <input
                          type="password"
                          className="form-input"
                          placeholder="Değiştirmek için doldurun"
                          value={settings.smtpPassword ?? ""}
                          onChange={(e) => setSettings({ ...settings, smtpPassword: e.target.value })}
                        />
                      </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="form-label">Gönderen E-Posta</label>
                        <input
                          type="email"
                          className="form-input"
                          placeholder="admin@nexmote.com"
                          value={settings.smtpFromAddress ?? ""}
                          onChange={(e) => setSettings({ ...settings, smtpFromAddress: e.target.value })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="form-label">Gönderen Adı</label>
                        <input
                          type="text"
                          className="form-input"
                          placeholder="NexMote RMM"
                          value={settings.smtpFromName ?? ""}
                          onChange={(e) => setSettings({ ...settings, smtpFromName: e.target.value })}
                        />
                      </div>
                    </div>

                    <div className="row-action-group" style={{ marginTop: "var(--space-2)" }}>
                      <button type="submit" className="btn-primary" data-width="fixed" disabled={savingSettings}>
                        <Save size={14} />
                        {savingSettings ? "Kaydediliyor..." : "SMTP Ayarlarını Kaydet"}
                      </button>
                    </div>
                  </form>

                  <div className="settings-form" style={{ marginTop: "var(--space-4)", paddingTop: "var(--space-4)", borderTop: "1px solid var(--border-subtle)" }}>
                    <div className="form-group">
                      <label className="form-label">Test E-postası Gönder</label>
                      <div style={{ display: "flex", gap: "var(--space-2)" }}>
                        <input
                          type="email"
                          className="form-input"
                          placeholder="test@ornek.com"
                          value={smtpTestEmail}
                          onChange={(e) => setSmtpTestEmail(e.target.value)}
                        />
                        <button type="button" className="btn-secondary" onClick={handleTestSmtp} disabled={testingSmtp}>
                          {testingSmtp ? "Gönderiliyor..." : "Test Gönder"}
                        </button>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="content-card">
                  <h2 className="content-card-title">Otomatik Cihaz Uyarıları</h2>
                  <p className="content-card-copy">
                    Bir cihaz çevrimdışı kaldığında veya disk/CPU/RAM eşiği aşıldığında SMTP üzerinden otomatik bildirim e-postası gönderilir.
                  </p>

                  <form onSubmit={handleSaveSettings} className="settings-form">
                    <div className="login-options-row">
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={settings.alertsEnabled}
                          onChange={(e) => setSettings({ ...settings, alertsEnabled: e.target.checked })}
                        />
                        <strong>Uyarı Sistemi Açık</strong>
                      </label>
                    </div>
                    <div className="form-group">
                      <label className="form-label">Bildirim Alıcı E-postaları</label>
                      <input
                        type="text"
                        className="form-input"
                        placeholder="Boş bırakılırsa tüm Yöneticilere (Admin) gönderilir"
                        value={settings.alertRecipientEmails ?? ""}
                        onChange={(e) => setSettings({ ...settings, alertRecipientEmails: e.target.value })}
                      />
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="remember-label" style={{ marginBottom: 6 }}>
                          <input
                            type="checkbox"
                            checked={settings.alertOfflineEnabled}
                            onChange={(e) => setSettings({ ...settings, alertOfflineEnabled: e.target.checked })}
                          />
                          Çevrimdışı Uyarısı (dk)
                        </label>
                        <input
                          type="number"
                          className="form-input"
                          min={1}
                          value={settings.alertOfflineMinutes}
                          onChange={(e) => setSettings({ ...settings, alertOfflineMinutes: Number(e.target.value) })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="remember-label" style={{ marginBottom: 6 }}>
                          <input
                            type="checkbox"
                            checked={settings.alertDiskLowEnabled}
                            onChange={(e) => setSettings({ ...settings, alertDiskLowEnabled: e.target.checked })}
                          />
                          Disk Az Uyarısı (MB)
                        </label>
                        <input
                          type="number"
                          className="form-input"
                          min={0}
                          value={settings.alertDiskLowMb}
                          onChange={(e) => setSettings({ ...settings, alertDiskLowMb: Number(e.target.value) })}
                        />
                      </div>
                    </div>

                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="remember-label" style={{ marginBottom: 6 }}>
                          <input
                            type="checkbox"
                            checked={settings.alertCpuHighEnabled}
                            onChange={(e) => setSettings({ ...settings, alertCpuHighEnabled: e.target.checked })}
                          />
                          CPU Yüksek Uyarısı (%)
                        </label>
                        <input
                          type="number"
                          className="form-input"
                          min={1}
                          max={100}
                          value={settings.alertCpuHighPercent}
                          onChange={(e) => setSettings({ ...settings, alertCpuHighPercent: Number(e.target.value) })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="remember-label" style={{ marginBottom: 6 }}>
                          <input
                            type="checkbox"
                            checked={settings.alertMemoryHighEnabled}
                            onChange={(e) => setSettings({ ...settings, alertMemoryHighEnabled: e.target.checked })}
                          />
                          RAM Yüksek Uyarısı (%)
                        </label>
                        <input
                          type="number"
                          className="form-input"
                          min={1}
                          max={100}
                          value={settings.alertMemoryHighPercent}
                          onChange={(e) => setSettings({ ...settings, alertMemoryHighPercent: Number(e.target.value) })}
                        />
                      </div>
                    </div>

                    <button type="submit" className="btn-primary" data-size="lg" data-width="fixed" disabled={savingSettings}>
                      <Save size={14} />
                      {savingSettings ? "Kaydediliyor..." : "Uyarı Ayarlarını Kaydet"}
                    </button>
                  </form>
                </div>
              </>
            )}

            {/* TAB 3: HESABIM & GÜVENLİK */}
            {settingsActiveTab === "account" && (
              <>
                <div className="content-card">
                  <h2 className="content-card-title">Hesap Bilgileri &amp; Şifre Değiştir</h2>
                  <p className="content-card-copy">Giriş şifrenizi güncelleyin.</p>

                  <form onSubmit={handleChangePassword} className="settings-form">
                    <div className="form-group">
                      <label className="form-label">Mevcut Şifre</label>
                      <input
                        type="password"
                        className="form-input"
                        value={accountCurrentPassword}
                        onChange={(e) => setAccountCurrentPassword(e.target.value)}
                        required
                      />
                    </div>
                    <div className="form-group">
                      <label className="form-label">Yeni Şifre (En az 8 karakter)</label>
                      <input
                        type="password"
                        className="form-input"
                        value={accountNewPassword}
                        onChange={(e) => setAccountNewPassword(e.target.value)}
                        minLength={8}
                        required
                      />
                    </div>
                    <div className="form-group">
                      <label className="form-label">Yeni Şifreyi Doğrulayın</label>
                      <input
                        type="password"
                        className="form-input"
                        value={accountConfirmNewPassword}
                        onChange={(e) => setAccountConfirmNewPassword(e.target.value)}
                        minLength={8}
                        required
                      />
                    </div>
                    <button type="submit" className="btn-primary" data-size="lg" data-width="fixed" disabled={accountBusy}>
                      <KeyRound size={14} />
                      {accountBusy ? "Kaydediliyor..." : "Şifreyi Değiştir"}
                    </button>
                  </form>
                </div>

                <div className="content-card">
                  <h2 className="content-card-title">İki Faktörlü Doğrulama (TOTP MFA)</h2>
                  <p className="content-card-copy">
                    {currentUser?.mfaEnabled
                      ? "MFA hesabınızda etkin. Her oturum açışta mobil authenticator kodunuz istenir."
                      : "MFA hesabınızı korumak için önerilir. Etkinleştirmek için Google/Microsoft Authenticator uygulamasıyla QR kodu okutun."}
                  </p>

                  {mfaRecoveryCodes && (
                    <div className="stale-data-notice">
                      <AlertCircle size={14} />
                      <div>
                        <p style={{ margin: "0 0 6px" }}>
                          <strong>Kurtarma kodlarınız (bir kereliğine gösteriliyor, güvenli yere kaydedin):</strong>
                        </p>
                        <div className="recovery-codes-grid">
                          {mfaRecoveryCodes.map((code) => (
                            <code key={code}>{code}</code>
                          ))}
                        </div>
                      </div>
                    </div>
                  )}

                  {currentUser?.mfaEnabled ? (
                    <form onSubmit={handleDisableMfa} className="settings-form">
                      <div className="form-group">
                        <label className="form-label">MFA'yı kapatmak için şifrenizi girin</label>
                        <input
                          type="password"
                          className="form-input"
                          value={mfaDisablePassword}
                          onChange={(e) => setMfaDisablePassword(e.target.value)}
                          required
                        />
                      </div>
                      <button type="submit" className="btn-secondary" disabled={accountBusy}>
                        {accountBusy ? "İşleniyor..." : "MFA'yı Kapat"}
                      </button>
                    </form>
                  ) : mfaSetupQr ? (
                    <form onSubmit={handleEnableMfa} className="settings-form">
                      <img src={mfaSetupQr} alt="MFA QR kodu" style={{ width: 220, height: 220, borderRadius: 8 }} />
                      {mfaSetupSecret && (
                        <p className="form-help">Manuel giriş anahtarı: <code>{mfaSetupSecret}</code></p>
                      )}
                      <div className="form-group">
                        <label className="form-label">Authenticator uygulamasındaki 6 haneli kod</label>
                        <input
                          type="text"
                          inputMode="numeric"
                          className="form-input"
                          value={mfaEnableCode}
                          onChange={(e) => setMfaEnableCode(e.target.value)}
                          required
                        />
                      </div>
                      <button type="submit" className="btn-primary" data-width="fixed" disabled={accountBusy}>
                        {accountBusy ? "Doğrulanıyor..." : "MFA'yı Etkinleştir"}
                      </button>
                    </form>
                  ) : (
                    <button className="btn-primary" data-width="fixed" onClick={handleStartMfaSetup} disabled={accountBusy}>
                      <Shield size={14} />
                      MFA Kurulumunu Başlat
                    </button>
                  )}
                </div>
              </>
            )}
          </div>
        )}

        {/* View 4: Kullanıcı Yönetimi (Admin) */}
        {view === "users" && currentUser?.role === "Admin" && (
          <UsersView
            currentUser={currentUser}
            newUserMode={newUserMode}
            setNewUserMode={setNewUserMode}
            createdUserCredentials={createdUserCredentials}
            invitedEmail={invitedEmail}
            newUserEmail={newUserEmail}
            setNewUserEmail={setNewUserEmail}
            newUserDisplayName={newUserDisplayName}
            setNewUserDisplayName={setNewUserDisplayName}
            newUserRole={newUserRole}
            setNewUserRole={setNewUserRole}
            creatingUser={creatingUser}
            handleCreateUser={handleCreateUser}
            users={users}
            handleSetUserRole={handleSetUserRole}
            handleToggleUserActive={handleToggleUserActive}
            handleResetUserMfa={handleResetUserMfa}
          />
        )}

        {/* View 5: Denetim Logu (Admin) */}
        {view === "audit-log" && currentUser?.role === "Admin" && (
          <AuditLogView
            auditEntries={auditEntries}
            auditTotal={auditTotal}
            auditPage={auditPage}
            auditPageSize={auditPageSize}
            auditLoading={auditLoading}
            refreshAuditLog={refreshAuditLog}
          />
        )}

        {/* View: Şirketler, Departmanlar & Güvenlik Yönetimi (Admin) */}
        {view === "device-groups" && currentUser?.role === "Admin" && (
          <div className="content-pane">
            {/* Üst Sekme Çubuğu */}
            <div className="settings-tabs-bar" style={{ marginBottom: "var(--space-3)" }}>
              <button
                type="button"
                className={`settings-tab-btn ${orgActiveTab === "companies" ? "active" : ""}`}
                onClick={() => setOrgActiveTab("companies")}
              >
                <Building2 size={15} /> Şirketler &amp; Departmanlar ({rootCompanies.length})
              </button>
              <button
                type="button"
                className={`settings-tab-btn ${orgActiveTab === "profiles" ? "active" : ""}`}
                onClick={() => setOrgActiveTab("profiles")}
              >
                <ShieldCheck size={15} /> Güvenlik Profilleri Kataloğu ({securityProfiles.length})
              </button>
            </div>

            {/* SEKME 1: ŞİRKETLER, DEPARTMANLAR & CİHAZLAR (UNIFIED TREE VIEW & QUICK INSPECTOR) */}
            {orgActiveTab === "companies" && (
              <div className="org-unified-layout">
                {/* SOL PANEL: Hiyerarşik Dizin Ağacı (Tree-View) */}
                <div className="org-tree-card">
                  {/* Toolbar & Arama */}
                  <div className="org-tree-toolbar">
                    <div className="table-search-wrapper" style={{ flex: 1, maxWidth: 280 }}>
                      <Search size={13} className="search-icon" />
                      <input
                        type="text"
                        className="table-search-input"
                        style={{ height: 30, fontSize: "12px", paddingLeft: 28 }}
                        placeholder="Şirket, departman veya cihaz ara..."
                        value={searchOrgQuery}
                        onChange={(e) => setSearchOrgQuery(e.target.value)}
                      />
                    </div>

                    <div style={{ display: "flex", gap: "6px" }}>
                      <button
                        type="button"
                        className="btn-secondary"
                        style={{ height: 30, fontSize: "11.5px", padding: "0 8px" }}
                        onClick={() => {
                          if (expandedTreeNodes.size > 0) {
                            collapseAllTreeNodes();
                          } else {
                            expandAllTreeNodes();
                          }
                        }}
                        title={expandedTreeNodes.size > 0 ? "Tümünü Daralt" : "Tümünü Genişlet"}
                      >
                        <ArrowUpDown size={12} /> {expandedTreeNodes.size > 0 ? "Daralt" : "Genişlet"}
                      </button>
                      <button
                        type="button"
                        className="btn-primary"
                        style={{ height: 30, fontSize: "11.5px", padding: "0 10px" }}
                        onClick={() => {
                          setNewCompanyName("");
                          setNewCompanyPolicyId("");
                          setShowNewCompanyModal(true);
                        }}
                      >
                        <Plus size={13} /> Yeni Şirket
                      </button>
                    </div>
                  </div>

                  {/* Ağaç Düğüm Listesi */}
                  <div className="org-tree-list">
                    {rootCompanies.length === 0 ? (
                      <div style={{ textAlign: "center", padding: "40px 16px", color: "var(--text-dim)", fontSize: "12.5px" }}>
                        <Building2 size={36} style={{ color: "var(--text-dim)", margin: "0 auto 10px" }} />
                        <strong>Henüz kayıtlı şirket bulunmuyor.</strong>
                        <p style={{ margin: "4px 0 14px 0", fontSize: "11.5px" }}>
                          Organizasyonunuzu kurmak için yukarıdaki <strong>+ Yeni Şirket</strong> butonuna tıklayın.
                        </p>
                        <button
                          type="button"
                          className="btn-primary"
                          onClick={() => {
                            setNewCompanyName("");
                            setNewCompanyPolicyId("");
                            setShowNewCompanyModal(true);
                          }}
                        >
                          + İlk Şirketi Ekle
                        </button>
                      </div>
                    ) : (
                      rootCompanies
                        .filter((comp) => {
                          if (!searchOrgQuery.trim()) return true;
                          const q = searchOrgQuery.toLowerCase();
                          const compMatch = comp.name.toLowerCase().includes(q);
                          const depts = deviceGroups.filter((g) => g.parentGroupId === comp.id);
                          const deptMatch = depts.some((d) => d.name.toLowerCase().includes(q));
                          const devMatch = devices.some(
                            (d) =>
                              (d.groupId === comp.id || depts.some((dept) => dept.id === d.groupId)) &&
                              (d.deviceName.toLowerCase().includes(q) ||
                                (d.ipAddress && d.ipAddress.toLowerCase().includes(q)) ||
                                (d.activeUser && d.activeUser.toLowerCase().includes(q)))
                          );
                          return compMatch || deptMatch || devMatch;
                        })
                        .map((comp) => {
                          const compDepts = deviceGroups.filter((g) => g.parentGroupId === comp.id);
                          const compDirectDevices = devices.filter((d) => d.groupId === comp.id);
                          const compTotalDevices = devices.filter(
                            (d) => d.groupId === comp.id || compDepts.some((dept) => dept.id === d.groupId)
                          );
                          const isCompSelected =
                            selectedTreeTarget?.type === "company" && selectedTreeTarget?.id === comp.id;
                          const isCompExpanded =
                            expandedTreeNodes.has(comp.id) || searchOrgQuery.trim().length > 0;
                          const matchedProfile = securityProfiles.find((p) => p.id === comp.defaultSecurityProfileId);

                          return (
                            <div
                              key={comp.id}
                              className={`tree-node-company ${isCompSelected ? "selected" : ""}`}
                            >
                              {/* Şirket Satırı (Root Node) */}
                              <div
                                className={`tree-row ${isCompSelected ? "active" : ""}`}
                                onClick={() => {
                                  setSelectedOrgCompanyId(comp.id);
                                  setSelectedTreeTarget({ type: "company", id: comp.id });
                                }}
                              >
                                <div className="tree-row-left">
                                  <button
                                    type="button"
                                    className={`tree-chevron-btn ${isCompExpanded ? "expanded" : ""}`}
                                    onClick={(e) => {
                                      e.stopPropagation();
                                      toggleTreeNode(comp.id);
                                    }}
                                    title={isCompExpanded ? "Daralt" : "Genişlet"}
                                  >
                                    <ChevronRight size={14} />
                                  </button>

                                  <Building2 size={16} style={{ color: "var(--primary)", flexShrink: 0 }} />
                                  <span className="tree-node-title" title={comp.name}>
                                    {comp.name}
                                  </span>

                                  <span className="version-pill" style={{ fontSize: "10.5px" }}>
                                    🖥️ {compTotalDevices.length}
                                  </span>
                                  <span className="version-pill" style={{ fontSize: "10.5px" }}>
                                    📁 {compDepts.length}
                                  </span>

                                  {matchedProfile && (
                                    <span
                                      className="shield-tag"
                                      style={{
                                        fontSize: "10px",
                                        padding: "1px 6px",
                                        background:
                                          matchedProfile.consentMode === "always_prompt"
                                            ? "rgba(245, 158, 11, 0.12)"
                                            : matchedProfile.consentMode === "prompt_if_active"
                                            ? "rgba(59, 130, 246, 0.12)"
                                            : "rgba(34, 197, 94, 0.12)",
                                        color:
                                          matchedProfile.consentMode === "always_prompt"
                                            ? "#d97706"
                                            : matchedProfile.consentMode === "prompt_if_active"
                                            ? "var(--primary)"
                                            : "#16a34a"
                                      }}
                                    >
                                      🛡️ {matchedProfile.name}
                                    </span>
                                  )}
                                </div>

                                <div className="tree-row-actions" onClick={(e) => e.stopPropagation()}>
                                  <button
                                    type="button"
                                    className="btn-secondary"
                                    style={{ height: 24, fontSize: "11px", padding: "0 6px" }}
                                    title="Bu şirkete yeni departman ekle"
                                    onClick={() => {
                                      setSelectedOrgCompanyId(comp.id);
                                      setNewDeptCompanyId(comp.id);
                                      setNewDeptName("");
                                      setNewDeptPolicyId("");
                                      setShowNewDeptModal(true);
                                    }}
                                  >
                                    <FolderPlus size={12} /> + Departman
                                  </button>

                                  <button
                                    type="button"
                                    className="icon-action-btn"
                                    title="Şirketi Yeniden Adlandır"
                                    onClick={() => handleOpenEditGroup(comp)}
                                  >
                                    <KeyRound size={12} />
                                  </button>

                                  <button
                                    type="button"
                                    className="icon-action-btn btn-danger-subtle"
                                    title="Şirketi Sil"
                                    onClick={() => handleDeleteGroup(comp)}
                                  >
                                    <Trash2 size={12} />
                                  </button>
                                </div>
                              </div>

                              {/* Departmanlar ve Cihazlar Alt Düğümleri */}
                              {isCompExpanded && (
                                <div className="tree-dept-container">
                                  {compDepts.length === 0 && compDirectDevices.length === 0 ? (
                                    <div
                                      style={{
                                        fontSize: "11.5px",
                                        color: "var(--text-dim)",
                                        padding: "6px 8px",
                                        display: "flex",
                                        alignItems: "center",
                                        justifyContent: "space-between"
                                      }}
                                    >
                                      <span>Bu şirkete henüz departman eklenmemiş.</span>
                                      <button
                                        type="button"
                                        className="btn-link"
                                        style={{ fontSize: "11px", color: "var(--primary)", background: "transparent", border: "none", cursor: "pointer", fontWeight: 600 }}
                                        onClick={() => {
                                          setSelectedOrgCompanyId(comp.id);
                                          setNewDeptCompanyId(comp.id);
                                          setNewDeptName("");
                                          setNewDeptPolicyId("");
                                          setShowNewDeptModal(true);
                                        }}
                                      >
                                        + Departman Ekle
                                      </button>
                                    </div>
                                  ) : (
                                    <>
                                      {/* Departman Listesi */}
                                      {compDepts.map((dept) => {
                                        const deptDevices = devices.filter((d) => d.groupId === dept.id);
                                        const isDeptSelected =
                                          selectedTreeTarget?.type === "dept" && selectedTreeTarget?.id === dept.id;
                                        const isDeptExpanded =
                                          expandedTreeNodes.has(dept.id) || searchOrgQuery.trim().length > 0;
                                        const deptProfile = securityProfiles.find(
                                          (p) => p.id === dept.defaultSecurityProfileId
                                        );

                                        return (
                                          <div
                                            key={dept.id}
                                            className={`tree-node-dept ${isDeptSelected ? "selected" : ""}`}
                                          >
                                            {/* Departman Satırı */}
                                            <div
                                              className={`tree-row ${isDeptSelected ? "active" : ""}`}
                                              onClick={() => {
                                                setSelectedOrgCompanyId(comp.id);
                                                setSelectedTreeTarget({ type: "dept", id: dept.id });
                                              }}
                                            >
                                              <div className="tree-row-left">
                                                <button
                                                  type="button"
                                                  className={`tree-chevron-btn ${isDeptExpanded ? "expanded" : ""}`}
                                                  onClick={(e) => {
                                                    e.stopPropagation();
                                                    toggleTreeNode(dept.id);
                                                  }}
                                                  title={isDeptExpanded ? "Daralt" : "Genişlet"}
                                                >
                                                  <ChevronRight size={13} />
                                                </button>

                                                <Folder size={15} style={{ color: "#d97706", flexShrink: 0 }} />
                                                <span className="tree-node-title" title={dept.name}>
                                                  {dept.name}
                                                </span>

                                                <span className="version-pill" style={{ fontSize: "10px" }}>
                                                  🖥️ {deptDevices.length} Cihaz
                                                </span>

                                                {deptProfile ? (
                                                  <span className="shield-tag" style={{ fontSize: "10px", padding: "1px 6px" }}>
                                                    🛡️ {deptProfile.name}
                                                  </span>
                                                ) : (
                                                  <span style={{ fontSize: "10px", color: "var(--text-dim)" }}>
                                                    (Miras Al)
                                                  </span>
                                                )}
                                              </div>

                                              <div className="tree-row-actions" onClick={(e) => e.stopPropagation()}>
                                                <button
                                                  type="button"
                                                  className="btn-secondary"
                                                  style={{ height: 22, fontSize: "10.5px", padding: "0 6px" }}
                                                  title="Bu departmana cihaz ata"
                                                  onClick={() => handleOpenAssignDevices(dept)}
                                                >
                                                  <Plus size={11} /> Cihaz Ata
                                                </button>

                                                <button
                                                  type="button"
                                                  className="icon-action-btn"
                                                  title="Departmanı Yeniden Adlandır"
                                                  onClick={() => handleOpenEditGroup(dept)}
                                                >
                                                  <KeyRound size={12} />
                                                </button>

                                                <button
                                                  type="button"
                                                  className="icon-action-btn btn-danger-subtle"
                                                  title="Departmanı Sil"
                                                  onClick={() => handleDeleteGroup(dept)}
                                                >
                                                  <Trash2 size={12} />
                                                </button>
                                              </div>
                                            </div>

                                            {/* Departmana Bağlı Cihazlar Listesi */}
                                            {isDeptExpanded && (
                                              <div className="tree-devices-container">
                                                {deptDevices.length === 0 ? (
                                                  <div
                                                    style={{
                                                      fontSize: "11px",
                                                      color: "var(--text-dim)",
                                                      padding: "4px 8px",
                                                      display: "flex",
                                                      alignItems: "center",
                                                      justifyContent: "space-between"
                                                    }}
                                                  >
                                                    <span>Bu departmanda henüz cihaz yok.</span>
                                                    <button
                                                      type="button"
                                                      className="btn-link"
                                                      style={{ fontSize: "10.5px", color: "var(--primary)", background: "transparent", border: "none", cursor: "pointer", fontWeight: 600 }}
                                                      onClick={() => handleOpenAssignDevices(dept)}
                                                    >
                                                      + Cihaz Ekle
                                                    </button>
                                                  </div>
                                                ) : (
                                                  deptDevices.map((dev) => {
                                                    const isOnline =
                                                      Date.now() - new Date(dev.lastSeenAt).getTime() < 60000;

                                                    return (
                                                      <div key={dev.id} className="tree-device-item">
                                                        <div className="tree-device-item-left">
                                                          <span
                                                            className={`status-dot ${isOnline ? "online" : "offline"}`}
                                                            style={{ width: 6, height: 6 }}
                                                          />
                                                          <Laptop size={13} style={{ color: "var(--text-dim)" }} />
                                                          <strong
                                                            style={{ cursor: "pointer", color: "var(--text-main)" }}
                                                            onClick={() => {
                                                              setSelectedDeviceId(dev.id);
                                                              setView("device-detail");
                                                            }}
                                                            title="Cihaz Detayını Aç"
                                                          >
                                                            {dev.deviceName}
                                                          </strong>
                                                          <span style={{ fontSize: "10.5px", color: "var(--text-dim)" }}>
                                                            ({dev.ipAddress} · {dev.activeUser})
                                                          </span>
                                                        </div>

                                                        <div style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                                                          <button
                                                            type="button"
                                                            className="btn-secondary"
                                                            style={{ height: 20, fontSize: "10px", padding: "0 6px" }}
                                                            onClick={() => {
                                                              setSelectedDeviceId(dev.id);
                                                              setView("device-detail");
                                                            }}
                                                            title="Cihaz Detayını Aç"
                                                          >
                                                            Detay →
                                                          </button>

                                                          <button
                                                            type="button"
                                                            className="icon-action-btn btn-danger-subtle"
                                                            style={{ width: 20, height: 20 }}
                                                            title={`"${dev.deviceName}" cihazını departmandan çıkar`}
                                                            onClick={() => handleUnassignDevice(dev.id, dept.name)}
                                                          >
                                                            <X size={11} />
                                                          </button>
                                                        </div>
                                                      </div>
                                                    );
                                                  })
                                                )}
                                              </div>
                                            )}
                                          </div>
                                        );
                                      })}

                                      {/* Doğrudan Şirkete Atanmış (Departmansız) Cihazlar */}
                                      {compDirectDevices.length > 0 && (
                                        <div style={{ marginTop: "4px", padding: "4px 8px", background: "rgba(0,0,0,0.02)", borderRadius: 6 }}>
                                          <span style={{ fontSize: "10.5px", fontWeight: 600, color: "var(--text-dim)", display: "block", marginBottom: 4 }}>
                                            🏢 Doğrudan Şirkete Bağlı Cihazlar ({compDirectDevices.length}):
                                          </span>
                                          <div className="tree-devices-container" style={{ marginLeft: 0, paddingLeft: 8, borderLeft: "2px dotted var(--border-subtle)" }}>
                                            {compDirectDevices.map((dev) => {
                                              const isOnline = Date.now() - new Date(dev.lastSeenAt).getTime() < 60000;
                                              return (
                                                <div key={dev.id} className="tree-device-item">
                                                  <div className="tree-device-item-left">
                                                    <span className={`status-dot ${isOnline ? "online" : "offline"}`} style={{ width: 6, height: 6 }} />
                                                    <Laptop size={13} style={{ color: "var(--text-dim)" }} />
                                                    <strong
                                                      style={{ cursor: "pointer" }}
                                                      onClick={() => {
                                                        setSelectedDeviceId(dev.id);
                                                        setView("device-detail");
                                                      }}
                                                    >
                                                      {dev.deviceName}
                                                    </strong>
                                                    <span style={{ fontSize: "10.5px", color: "var(--text-dim)" }}>
                                                      ({dev.ipAddress} · {dev.activeUser})
                                                    </span>
                                                  </div>
                                                  <div style={{ display: "flex", alignItems: "center", gap: "4px" }}>
                                                    <button
                                                      type="button"
                                                      className="btn-secondary"
                                                      style={{ height: 20, fontSize: "10px", padding: "0 6px" }}
                                                      onClick={() => {
                                                        setSelectedDeviceId(dev.id);
                                                        setView("device-detail");
                                                      }}
                                                    >
                                                      Detay →
                                                    </button>
                                                    <button
                                                      type="button"
                                                      className="icon-action-btn btn-danger-subtle"
                                                      style={{ width: 20, height: 20 }}
                                                      title="Gruptan çıkar"
                                                      onClick={() => handleUnassignDevice(dev.id, comp.name)}
                                                    >
                                                      <X size={11} />
                                                    </button>
                                                  </div>
                                                </div>
                                              );
                                            })}
                                          </div>
                                        </div>
                                      )}
                                    </>
                                  )}
                                </div>
                              )}
                            </div>
                          );
                        })
                    )}
                  </div>
                </div>

                {/* SAĞ PANEL: Hızlı Denetim Kartı (Context Inspector) */}
                {(() => {
                  const activeTargetGroup =
                    (selectedTreeTarget ? deviceGroups.find((g) => g.id === selectedTreeTarget.id) : null) ||
                    selectedOrgCompany ||
                    rootCompanies[0] ||
                    null;

                  if (!activeTargetGroup) {
                    return (
                      <div className="org-inspector-card" style={{ textAlign: "center", padding: "40px 20px" }}>
                        <Building2 size={40} style={{ color: "var(--text-dim)", margin: "0 auto 12px" }} />
                        <h3 style={{ fontSize: "16px", fontWeight: 700, color: "var(--text-main)", marginBottom: 6 }}>
                          Grup Seçilmedi
                        </h3>
                        <p style={{ fontSize: "12px", color: "var(--text-dim)", margin: "0 auto 16px", maxWidth: 300 }}>
                          Sol taraftaki ağaçtan bir şirket veya departman seçerek güvenlik politikasını ve cihazlarını anında yönetebilirsiniz.
                        </p>
                        <button
                          type="button"
                          className="btn-primary"
                          onClick={() => {
                            setNewCompanyName("");
                            setNewCompanyPolicyId("");
                            setShowNewCompanyModal(true);
                          }}
                        >
                          + Yeni Şirket Ekle
                        </button>
                      </div>
                    );
                  }

                  const isCompany = !activeTargetGroup.parentGroupId;
                  const parentComp = !isCompany ? deviceGroups.find((g) => g.id === activeTargetGroup.parentGroupId) : null;
                  const currentProfile = securityProfiles.find((p) => p.id === activeTargetGroup.defaultSecurityProfileId);
                  const effectiveProfile = getDeviceEffectiveProfile(devices.find((d) => d.groupId === activeTargetGroup.id)) || currentProfile;
                  
                  const groupDepts = isCompany ? deviceGroups.filter((g) => g.parentGroupId === activeTargetGroup.id) : [];
                  const groupDevices = isCompany
                    ? devices.filter((d) => d.groupId === activeTargetGroup.id || groupDepts.some((dept) => dept.id === d.groupId))
                    : devices.filter((d) => d.groupId === activeTargetGroup.id);

                  return (
                    <div className="org-inspector-card">
                      {/* 1. Başlık & Hızlı Bilgi */}
                      <div className="inspector-header">
                        <div style={{ display: "flex", alignItems: "center", gap: "12px" }}>
                          <div className="inspector-badge-icon">
                            {isCompany ? <Building2 size={22} /> : <Folder size={22} />}
                          </div>
                          <div>
                            <div style={{ display: "flex", alignItems: "center", gap: "8px" }}>
                              <h2 style={{ fontSize: "16px", fontWeight: 700, color: "var(--text-main)", margin: 0 }}>
                                {activeTargetGroup.name}
                              </h2>
                              <button
                                type="button"
                                className="icon-action-btn"
                                title="Grubu Yeniden Adlandır"
                                onClick={() => handleOpenEditGroup(activeTargetGroup)}
                              >
                                <KeyRound size={13} />
                              </button>
                            </div>
                            <span style={{ fontSize: "11.5px", color: "var(--text-dim)" }}>
                              {isCompany ? "🏢 Ana Şirket / Müşteri" : `📁 ${parentComp?.name || "Şirket"} Departmanı`} ·{" "}
                              <strong>{groupDevices.length} Cihaz</strong>
                              {isCompany && ` · ${groupDepts.length} Departman`}
                            </span>
                          </div>
                        </div>

                        <div style={{ display: "flex", gap: "6px" }}>
                          {isCompany && (
                            <button
                              type="button"
                              className="btn-secondary"
                              style={{ height: 28, fontSize: "11.5px", padding: "0 10px" }}
                              onClick={() => {
                                setSelectedOrgCompanyId(activeTargetGroup.id);
                                setNewDeptCompanyId(activeTargetGroup.id);
                                setNewDeptName("");
                                setNewDeptPolicyId("");
                                setShowNewDeptModal(true);
                              }}
                            >
                              <Plus size={12} /> Departman
                            </button>
                          )}
                          <button
                            type="button"
                            className="btn-primary"
                            style={{ height: 28, fontSize: "11.5px", padding: "0 10px" }}
                            onClick={() => handleOpenAssignDevices(activeTargetGroup)}
                          >
                            <Plus size={12} /> Cihaz Ata ({groupDevices.length})
                          </button>
                        </div>
                      </div>

                      {/* 2. Tek Tıkla Güvenlik Politikası Belirleyici (Presets) */}
                      <div>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" }}>
                          <div>
                            <strong style={{ fontSize: "13px", color: "var(--text-main)" }}>🛡️ Güvenlik Politikası Kuralı</strong>
                            <div style={{ fontSize: "11px", color: "var(--text-dim)" }}>
                              Bağlantı anında teknisyene uygulanacak onay ve yetki kuralını tek tıkla seçin:
                            </div>
                          </div>
                          <button
                            type="button"
                            className="btn-link"
                            style={{ fontSize: "11.5px", color: "var(--primary)", background: "transparent", border: "none", cursor: "pointer", fontWeight: 600 }}
                            onClick={() =>
                              openProfileEditorFor(activeTargetGroup.defaultSecurityProfileId, {
                                type: isCompany ? "company" : "dept",
                                id: activeTargetGroup.id,
                                name: activeTargetGroup.name
                              })
                            }
                          >
                            <Sliders size={12} /> Detaylı İnce Ayar
                          </button>
                        </div>

                        <div className="policy-preset-grid">
                          {/* Preset 1: Doğrudan Bağlantı */}
                          <div
                            className={`policy-preset-card ${
                              currentProfile?.consentMode === "unattended" && !currentProfile?.viewOnlyMode ? "active" : ""
                            }`}
                            onClick={() => handleApplyPolicyPreset(activeTargetGroup, "unattended")}
                          >
                            <div className="policy-preset-title">
                              <span>🟢 Doğrudan Bağlan</span>
                              {currentProfile?.consentMode === "unattended" && !currentProfile?.viewOnlyMode && (
                                <Check size={14} style={{ color: "var(--primary)" }} />
                              )}
                            </div>
                            <span className="policy-preset-desc">
                              Kullanıcıya sormadan anında bağlanır (IT &amp; Sunucular için ideal).
                            </span>
                          </div>

                          {/* Preset 2: Her Zaman Onay İste */}
                          <div
                            className={`policy-preset-card ${
                              currentProfile?.consentMode === "always_prompt" && !currentProfile?.viewOnlyMode ? "active" : ""
                            }`}
                            onClick={() => handleApplyPolicyPreset(activeTargetGroup, "always_prompt")}
                          >
                            <div className="policy-preset-title">
                              <span>🟡 Onay İste (30s)</span>
                              {currentProfile?.consentMode === "always_prompt" && !currentProfile?.viewOnlyMode && (
                                <Check size={14} style={{ color: "var(--primary)" }} />
                              )}
                            </div>
                            <span className="policy-preset-desc">
                              Kullanıcı ekrandan "Kabul Et" demeden bağlanamaz (Personel &amp; Muhasebe).
                            </span>
                          </div>

                          {/* Preset 3: Aktifken Sor */}
                          <div
                            className={`policy-preset-card ${
                              currentProfile?.consentMode === "prompt_if_active" && !currentProfile?.viewOnlyMode ? "active" : ""
                            }`}
                            onClick={() => handleApplyPolicyPreset(activeTargetGroup, "prompt_if_active")}
                          >
                            <div className="policy-preset-title">
                              <span>🔵 Aktifken Sor</span>
                              {currentProfile?.consentMode === "prompt_if_active" && !currentProfile?.viewOnlyMode && (
                                <Check size={14} style={{ color: "var(--primary)" }} />
                              )}
                            </div>
                            <span className="policy-preset-desc">
                              Kullanıcı bilgisayar başındaysa sorar, ekran kilitliyse direkt bağlanır.
                            </span>
                          </div>

                          {/* Preset 4: Sadece İzleme Modu */}
                          <div
                            className={`policy-preset-card ${currentProfile?.viewOnlyMode ? "active" : ""}`}
                            onClick={() => handleApplyPolicyPreset(activeTargetGroup, "view_only")}
                          >
                            <div className="policy-preset-title">
                              <span>👁️ Sadece İzle</span>
                              {currentProfile?.viewOnlyMode && <Check size={14} style={{ color: "var(--primary)" }} />}
                            </div>
                            <span className="policy-preset-desc">
                              Klavye ve fareyi kilitler, teknisyen sadece ekrandan izleme yapar.
                            </span>
                          </div>

                          {/* Departmansa Miras Al Seçeneği */}
                          {!isCompany && (
                            <div
                              className={`policy-preset-card ${!activeTargetGroup.defaultSecurityProfileId ? "active" : ""}`}
                              style={{ gridColumn: "1 / -1" }}
                              onClick={() => handleApplyPolicyPreset(activeTargetGroup, "inherit")}
                            >
                              <div className="policy-preset-title">
                                <span>🏢 Şirketten Miras Al ({parentComp?.name || "Şirket Varsayılanı"})</span>
                                {!activeTargetGroup.defaultSecurityProfileId && (
                                  <Check size={14} style={{ color: "var(--primary)" }} />
                                )}
                              </div>
                              <span className="policy-preset-desc">
                                Özel bir kural tanımlanmaz; şirket politikasını otomatik takip eder.
                              </span>
                            </div>
                          )}
                        </div>

                        {/* Aktif Politika Rozet Özeti */}
                        {currentProfile && (
                          <div className="policy-chips-row" style={{ marginTop: 8 }}>
                            <span className="shield-tag" style={{ fontSize: "11px" }}>
                              🛡️ Seçili Profil: <strong>{currentProfile.name}</strong>
                            </span>
                            {currentProfile.allowRemoteTerminal && (
                              <span className="version-pill" style={{ fontSize: "10.5px", color: "#16a34a" }}>
                                Terminal ✔
                              </span>
                            )}
                            {currentProfile.allowClipboard && (
                              <span className="version-pill" style={{ fontSize: "10.5px", color: "#16a34a" }}>
                                Pano ✔
                              </span>
                            )}
                            {currentProfile.requirePassword && (
                              <span className="version-pill" style={{ fontSize: "10.5px", color: "#f59e0b" }}>
                                🔒 Şifreli
                              </span>
                            )}
                          </div>
                        )}
                      </div>

                      {/* 3. Bu Gruptaki Cihazlar Listesi */}
                      <div style={{ borderTop: "1px solid var(--border-subtle)", paddingTop: "var(--space-3)" }}>
                        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "8px" }}>
                          <strong style={{ fontSize: "13px", color: "var(--text-main)" }}>
                            🖥️ Bu Gruptaki Cihazlar ({groupDevices.length})
                          </strong>
                          <button
                            type="button"
                            className="btn-secondary"
                            style={{ height: 26, fontSize: "11px", padding: "0 8px" }}
                            onClick={() => handleOpenAssignDevices(activeTargetGroup)}
                          >
                            <Plus size={12} /> Cihaz Ekle / Taşı
                          </button>
                        </div>

                        {groupDevices.length === 0 ? (
                          <div style={{ textAlign: "center", padding: "24px 12px", background: "var(--bg-hover)", borderRadius: 8, color: "var(--text-dim)", fontSize: "12px" }}>
                            Bu grupta henüz atanmış bilgisayar bulunmuyor.<br />
                            Yukarıdaki <strong>+ Cihaz Ekle / Taşı</strong> butonuna basarak cihazlarınızı buraya bağlayabilirsiniz.
                          </div>
                        ) : (
                          <div style={{ display: "flex", flexDirection: "column", gap: "6px", maxHeight: 240, overflowY: "auto" }}>
                            {groupDevices.map((dev) => {
                              const isOnline = Date.now() - new Date(dev.lastSeenAt).getTime() < 60000;
                              return (
                                <div
                                  key={dev.id}
                                  style={{
                                    display: "flex",
                                    alignItems: "center",
                                    justifyContent: "space-between",
                                    padding: "8px 12px",
                                    background: "var(--bg-hover)",
                                    borderRadius: 6,
                                    border: "1px solid var(--border-subtle)",
                                    fontSize: "12px"
                                  }}
                                >
                                  <div style={{ display: "flex", alignItems: "center", gap: "8px", overflow: "hidden" }}>
                                    <span className={`status-dot ${isOnline ? "online" : "offline"}`} style={{ width: 7, height: 7 }} />
                                    <Laptop size={14} style={{ color: "var(--text-dim)" }} />
                                    <strong style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }} title={dev.deviceName}>
                                      {dev.deviceName}
                                    </strong>
                                    <span style={{ fontSize: "11px", color: "var(--text-dim)" }}>
                                      ({dev.ipAddress} · {dev.activeUser})
                                    </span>
                                  </div>

                                  <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                                    <button
                                      type="button"
                                      className="btn-secondary"
                                      style={{ height: 22, fontSize: "10.5px", padding: "0 6px" }}
                                      onClick={() => {
                                        setSelectedDeviceId(dev.id);
                                        setView("device-detail");
                                      }}
                                    >
                                      Detay →
                                    </button>
                                    <button
                                      type="button"
                                      className="icon-action-btn btn-danger-subtle"
                                      style={{ width: 22, height: 22 }}
                                      title="Bu gruptan çıkar"
                                      onClick={() => handleUnassignDevice(dev.id, activeTargetGroup.name)}
                                    >
                                      <X size={12} />
                                    </button>
                                  </div>
                                </div>
                              );
                            })}
                          </div>
                        )}
                      </div>
                    </div>
                  );
                })()}
              </div>
            )}

            {/* SEKME 2: GÜVENLİK PROFİLLERİ KATALOĞU (GELİŞMİŞ BENTO KARTLAR) */}
            {orgActiveTab === "profiles" && (
              <div style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
                {/* Üst Bar: Arama ve Yeni Profil Butonu */}
                <div className="content-card" style={{ margin: 0, padding: "14px 18px" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", flexWrap: "wrap", gap: "12px" }}>
                    <div>
                      <h2 className="content-card-title" style={{ margin: 0 }}>Güvenlik Profilleri Kataloğu ({securityProfiles.length})</h2>
                      <p className="content-card-copy" style={{ margin: "2px 0 0 0" }}>
                        Farklı müşteri, departman veya sunucu grupları için tanımlanmış bağlantı onay ve koruma şablonları.
                      </p>
                    </div>

                    <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                      <div className="table-search-wrapper" style={{ width: 220 }}>
                        <Search size={13} className="search-icon" />
                        <input
                          type="text"
                          className="table-search-input"
                          style={{ height: 30, fontSize: "12px", paddingLeft: 28 }}
                          placeholder="Profil adı veya kural ara..."
                          value={searchProfileQuery}
                          onChange={(e) => setSearchProfileQuery(e.target.value)}
                        />
                      </div>
                      <button
                        type="button"
                        className="btn-primary"
                        style={{ height: 30, fontSize: "12px", padding: "0 12px" }}
                        onClick={() => openProfileEditorFor(null, { type: "standalone" })}
                      >
                        <Shield size={13} /> + Yeni Güvenlik Profili
                      </button>
                    </div>
                  </div>
                </div>

                {/* Profil Kartları Grid'i */}
                <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(340px, 1fr))", gap: "var(--space-3)" }}>
                  {securityProfiles
                    .filter(p => !searchProfileQuery.trim() || p.name.toLowerCase().includes(searchProfileQuery.toLowerCase()) || (p.agentDisplayName && p.agentDisplayName.toLowerCase().includes(searchProfileQuery.toLowerCase())))
                    .map((profile) => {
                      const stats = getProfileUsageStats(profile.id);

                      return (
                        <div key={profile.id} className="content-card" style={{ margin: 0, display: "flex", flexDirection: "column", justifyContent: "space-between", gap: "14px" }}>
                          {/* Kart Başlığı */}
                          <div>
                            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "8px" }}>
                              <div style={{ display: "flex", alignItems: "center", gap: "10px" }}>
                                {profile.iconBase64 ? (
                                  <img
                                    src={`data:image/png;base64,${profile.iconBase64}`}
                                    alt={profile.name}
                                    style={{ width: 28, height: 28, borderRadius: 6, border: "1px solid var(--border-subtle)" }}
                                  />
                                ) : (
                                  <div className="org-company-badge-icon" style={{ width: 28, height: 28 }}>
                                    <ShieldCheck size={16} />
                                  </div>
                                )}
                                <div>
                                  <h3 style={{ fontSize: "15px", fontWeight: 700, color: "var(--text-main)", margin: 0 }}>
                                    {profile.name}
                                  </h3>
                                  <span style={{ fontSize: "11px", color: "var(--text-dim)" }}>
                                    🏷️ {profile.agentDisplayName || "NexMote Agent"}
                                  </span>
                                </div>
                              </div>

                              <span
                                className="shield-tag"
                                style={{
                                  fontSize: "10.5px",
                                  background: profile.consentMode === "always_prompt"
                                    ? "rgba(245, 158, 11, 0.12)"
                                    : profile.consentMode === "prompt_if_active"
                                    ? "rgba(59, 130, 246, 0.12)"
                                    : "rgba(34, 197, 94, 0.12)",
                                  color: profile.consentMode === "always_prompt"
                                    ? "#d97706"
                                    : profile.consentMode === "prompt_if_active"
                                    ? "var(--primary)"
                                    : "#16a34a"
                                }}
                              >
                                {profile.consentMode === "always_prompt"
                                  ? `🟡 Onaylı (${profile.consentTimeoutSeconds}s)`
                                  : profile.consentMode === "prompt_if_active"
                                  ? `🔵 Aktifken Onay (${profile.consentTimeoutSeconds}s)`
                                  : "🟢 Doğrudan Erişim"}
                              </span>
                            </div>

                            {/* Canlı Etki Analizi */}
                            <div style={{ display: "flex", gap: "6px", flexWrap: "wrap", margin: "10px 0", fontSize: "11px" }}>
                              <span className="version-pill" style={{ background: stats.totalImpactedDevices > 0 ? "rgba(37, 99, 235, 0.08)" : undefined, borderColor: stats.totalImpactedDevices > 0 ? "rgba(37, 99, 235, 0.2)" : undefined, color: stats.totalImpactedDevices > 0 ? "var(--primary)" : undefined }}>
                                🎯 <strong>{stats.totalImpactedDevices} Cihaz</strong>
                              </span>
                              <span className="version-pill">
                                🏢 {stats.directCompanies} Şirket
                              </span>
                              <span className="version-pill">
                                📁 {stats.directDepts} Departman
                              </span>
                            </div>

                            {/* İzinler ve Güvenlik Matrisi */}
                            <div style={{ display: "flex", flexDirection: "column", gap: "6px", background: "var(--bg-hover)", padding: "10px", borderRadius: 8, fontSize: "11.5px" }}>
                              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <span style={{ color: "var(--text-dim)" }}>Kontrol Yetkisi:</span>
                                <strong>{profile.viewOnlyMode ? "👁️ Sadece İzleme" : "⚡ Tam Kontrol (Klavye/Fare)"}</strong>
                              </div>
                              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <span style={{ color: "var(--text-dim)" }}>Uzak Terminal:</span>
                                <span style={{ color: profile.allowRemoteTerminal ? "#16a34a" : "#dc2626", fontWeight: 600 }}>
                                  {profile.allowRemoteTerminal ? "✔ İzin Verildi" : "✖ Engellendi"}
                                </span>
                              </div>
                              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <span style={{ color: "var(--text-dim)" }}>Pano / Dosya Transferi:</span>
                                <span style={{ color: profile.allowClipboard && profile.allowFileTransfer ? "#16a34a" : "#d97706", fontWeight: 600 }}>
                                  {profile.allowClipboard ? "Pano ✔ " : "Pano ✖ "}{profile.allowFileTransfer ? "· Dosya ✔" : "· Dosya ✖"}
                                </span>
                              </div>
                              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                                <span style={{ color: "var(--text-dim)" }}>Şifre &amp; Menü Koruma:</span>
                                <span>
                                  {profile.requirePassword ? "🔒 Parolalı" : "🔓 Parolasız"} · {profile.restrictTrayMenu ? "🛡️ Kısıtlı Menü" : "Standart Menü"}
                                </span>
                              </div>
                            </div>
                          </div>

                          {/* Aksiyon Butonları */}
                          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", borderTop: "1px solid var(--border-subtle)", paddingTop: "10px" }}>
                            <div style={{ display: "flex", gap: "6px" }}>
                              <button
                                type="button"
                                className="btn-secondary"
                                style={{ height: 26, fontSize: "11.5px", padding: "0 8px" }}
                                onClick={() => handleCloneProfile(profile)}
                                title="Bu profili şablon olarak çoğalt"
                              >
                                <Copy size={12} /> Klonla
                              </button>
                              <button
                                type="button"
                                className="btn-secondary btn-danger-subtle"
                                style={{ height: 26, fontSize: "11.5px", padding: "0 8px" }}
                                onClick={() => handleDeleteProfile(profile)}
                                title="Profili Sil"
                              >
                                <Trash2 size={12} />
                              </button>
                            </div>

                            <button
                              type="button"
                              className="btn-primary"
                              style={{ height: 26, fontSize: "11.5px", padding: "0 12px" }}
                              onClick={() => handleEditProfile(profile)}
                            >
                              <Sliders size={12} /> Düzenle
                            </button>
                          </div>
                        </div>
                      );
                    })}
                </div>
              </div>
            )}
          </div>
        )}

        {/* Modal: Yeni Şirket Ekle */}
        {showNewCompanyModal && (
          <div className="modal-backdrop" onClick={() => setShowNewCompanyModal(false)}>
            <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div className="modal-title-with-icon">
                  <Building2 size={18} style={{ color: "var(--primary)" }} />
                  <div>
                    <h3 className="modal-title">Yeni Şirket Ekle</h3>
                    <p className="modal-subtitle">Müşteri veya ana organizasyon tanımlayın.</p>
                  </div>
                </div>
                <button className="modal-close-btn" onClick={() => setShowNewCompanyModal(false)}>
                  <X size={16} />
                </button>
              </div>
              <form onSubmit={handleCreateCompany}>
                <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
                  <div className="form-group">
                    <label className="form-label">Şirket Adı *</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="Örn: Talay Holding, Acme Teknoloji"
                      value={newCompanyName}
                      onChange={(e) => setNewCompanyName(e.target.value)}
                      required
                      autoFocus
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Varsayılan Ajan Güvenlik Politikası</label>
                    <select
                      className="form-input"
                      value={newCompanyPolicyId}
                      onChange={(e) => setNewCompanyPolicyId(e.target.value)}
                    >
                      <option value="">Standart / Kısıtlama Yok (Doğrudan Erişim)</option>
                      {securityProfiles.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.name}{" "}
                          {p.consentMode === "always_prompt"
                            ? "(🟡 Her Zaman Onay)"
                            : p.consentMode === "prompt_if_active"
                            ? "(🔵 Aktifken Onay)"
                            : "(🟢 Doğrudan)"}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div className="modal-footer">
                  <button type="button" className="btn-secondary" onClick={() => setShowNewCompanyModal(false)}>
                    İptal
                  </button>
                  <button type="submit" className="btn-primary" disabled={savingGroup}>
                    {savingGroup ? "Oluşturuluyor..." : "Şirket Oluştur"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Modal: Yeni Departman Ekle */}
        {showNewDeptModal && (
          <div className="modal-backdrop" onClick={() => setShowNewDeptModal(false)}>
            <div className="modal-dialog" style={{ maxWidth: 460 }} onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div className="modal-title-with-icon">
                  <FolderPlus size={18} style={{ color: "var(--primary)" }} />
                  <div>
                    <h3 className="modal-title">Yeni Departman Ekle</h3>
                    <p className="modal-subtitle">
                      Seçeceğiniz bir şirketin altına bağlı departman (şube/birim) oluşturun.
                    </p>
                  </div>
                </div>
                <button className="modal-close-btn" onClick={() => setShowNewDeptModal(false)}>
                  <X size={16} />
                </button>
              </div>
              <form onSubmit={handleCreateDepartment}>
                <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
                  <div className="form-group">
                    <label className="form-label">Bağlı Olacağı Şirket *</label>
                    <select
                      className="form-input"
                      value={newDeptCompanyId || selectedOrgCompany?.id || (rootCompanies[0]?.id ?? "")}
                      onChange={(e) => setNewDeptCompanyId(e.target.value)}
                      required
                    >
                      {rootCompanies.map((c) => (
                        <option key={c.id} value={c.id}>
                          🏢 {c.name}
                        </option>
                      ))}
                    </select>
                  </div>

                  <div className="form-group">
                    <label className="form-label">Departman Adı *</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="Örn: IT &amp; Sunucular, Muhasebe, Lojistik, İK"
                      value={newDeptName}
                      onChange={(e) => setNewDeptName(e.target.value)}
                      required
                      autoFocus
                    />
                  </div>

                  <div className="form-group">
                    <label className="form-label">Departman Güvenlik Politikası</label>
                    <select
                      className="form-input"
                      value={newDeptPolicyId}
                      onChange={(e) => setNewDeptPolicyId(e.target.value)}
                    >
                      <option value="">
                        — Miras Al (Şirket Varsayılan Güvenlik Politikası)
                      </option>
                      {securityProfiles.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.name}{" "}
                          {p.consentMode === "always_prompt"
                            ? "(🟡 Her Zaman Onay)"
                            : p.consentMode === "prompt_if_active"
                            ? "(🔵 Aktifken Onay)"
                            : "(🟢 Doğrudan)"}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div className="modal-footer">
                  <button type="button" className="btn-secondary" onClick={() => setShowNewDeptModal(false)}>
                    İptal
                  </button>
                  <button type="submit" className="btn-primary" disabled={savingGroup}>
                    {savingGroup ? "Oluşturuluyor..." : "Departman Oluştur"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Modal: Hızlı Cihaz Ata / Taşı (Assign Devices Modal) */}
        {showAssignDevicesModal && assignTargetGroup && (
          <div className="modal-backdrop" onClick={() => setShowAssignDevicesModal(false)}>
            <div className="modal-dialog" style={{ maxWidth: 520, maxHeight: "85vh", display: "flex", flexDirection: "column" }} onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div className="modal-title-with-icon">
                  <Laptop size={18} style={{ color: "var(--primary)" }} />
                  <div>
                    <h3 className="modal-title">Cihazları Ata / Taşı</h3>
                    <p className="modal-subtitle">
                      <strong>{assignTargetGroup.name}</strong> grubuna atanacak bilgisayarları seçin.
                    </p>
                  </div>
                </div>
                <button className="modal-close-btn" onClick={() => setShowAssignDevicesModal(false)}>
                  <X size={16} />
                </button>
              </div>

              <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "var(--space-2)", overflow: "hidden" }}>
                {/* Arama ve Toplu Seçim Barı */}
                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "8px" }}>
                  <div className="table-search-wrapper" style={{ flex: 1 }}>
                    <Search size={13} className="search-icon" />
                    <input
                      type="text"
                      className="table-search-input"
                      style={{ height: 28, fontSize: "11.5px", paddingLeft: 28 }}
                      placeholder="Cihaz adı, IP veya kullanıcı ara..."
                      value={deviceAssignSearchQuery}
                      onChange={(e) => setDeviceAssignSearchQuery(e.target.value)}
                      autoFocus
                    />
                  </div>

                  <div style={{ display: "flex", gap: "4px" }}>
                    <button
                      type="button"
                      className="btn-secondary"
                      style={{ height: 28, fontSize: "11px", padding: "0 8px" }}
                      onClick={() => {
                        const filtered = devices.filter((d) => {
                          if (!deviceAssignSearchQuery.trim()) return true;
                          const q = deviceAssignSearchQuery.toLowerCase();
                          return (
                            d.deviceName.toLowerCase().includes(q) ||
                            (d.ipAddress && d.ipAddress.toLowerCase().includes(q)) ||
                            (d.activeUser && d.activeUser.toLowerCase().includes(q))
                          );
                        });
                        setAssignSelectedDeviceIds(new Set([...assignSelectedDeviceIds, ...filtered.map((d) => d.id)]));
                      }}
                    >
                      Tümünü Seç
                    </button>
                    <button
                      type="button"
                      className="btn-secondary"
                      style={{ height: 28, fontSize: "11px", padding: "0 8px" }}
                      onClick={() => setAssignSelectedDeviceIds(new Set())}
                    >
                      Temizle
                    </button>
                  </div>
                </div>

                {/* Cihaz Seçim Listesi */}
                <div className="assign-device-list" style={{ maxHeight: 320, overflowY: "auto", border: "1px solid var(--border-subtle)", borderRadius: 6 }}>
                  {devices
                    .filter((d) => {
                      if (!deviceAssignSearchQuery.trim()) return true;
                      const q = deviceAssignSearchQuery.toLowerCase();
                      return (
                        d.deviceName.toLowerCase().includes(q) ||
                        (d.ipAddress && d.ipAddress.toLowerCase().includes(q)) ||
                        (d.activeUser && d.activeUser.toLowerCase().includes(q))
                      );
                    })
                    .map((dev) => {
                      const isSelected = assignSelectedDeviceIds.has(dev.id);
                      const isOnline = Date.now() - new Date(dev.lastSeenAt).getTime() < 60000;
                      const currentDevGroup = deviceGroups.find((g) => g.id === dev.groupId);

                      return (
                        <div
                          key={dev.id}
                          className={`assign-device-item ${isSelected ? "selected" : ""}`}
                          onClick={() => {
                            setAssignSelectedDeviceIds((prev) => {
                              const next = new Set(prev);
                              if (next.has(dev.id)) {
                                next.delete(dev.id);
                              } else {
                                next.add(dev.id);
                              }
                              return next;
                            });
                          }}
                        >
                          <div className="assign-device-checkbox">
                            {isSelected ? (
                              <CheckSquare size={16} style={{ color: "var(--primary)" }} />
                            ) : (
                              <Square size={16} style={{ color: "var(--text-dim)" }} />
                            )}
                          </div>

                          <span className={`status-dot ${isOnline ? "online" : "offline"}`} style={{ width: 6, height: 6 }} />

                          <div style={{ flex: 1, minWidth: 0 }}>
                            <div style={{ display: "flex", alignItems: "center", gap: "6px" }}>
                              <strong style={{ fontSize: "12px", color: "var(--text-main)" }}>{dev.deviceName}</strong>
                              <span style={{ fontSize: "11px", color: "var(--text-dim)" }}>({dev.ipAddress})</span>
                            </div>
                            <span style={{ fontSize: "10.5px", color: "var(--text-dim)" }}>
                              Kullanıcı: {dev.activeUser}
                            </span>
                          </div>

                          <div style={{ textAlign: "right" }}>
                            {currentDevGroup ? (
                              <span
                                className="version-pill"
                                style={{
                                  fontSize: "10px",
                                  background: currentDevGroup.id === assignTargetGroup.id ? "rgba(37, 99, 235, 0.1)" : undefined,
                                  color: currentDevGroup.id === assignTargetGroup.id ? "var(--primary)" : undefined
                                }}
                              >
                                {currentDevGroup.id === assignTargetGroup.id ? "✔ Bu Grupta" : currentDevGroup.name}
                              </span>
                            ) : (
                              <span style={{ fontSize: "10.5px", color: "var(--text-dim)" }}>Grupsuz</span>
                            )}
                          </div>
                        </div>
                      );
                    })}
                </div>

                <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", fontSize: "11.5px", color: "var(--text-dim)" }}>
                  <span>Seçili Cihaz: <strong>{assignSelectedDeviceIds.size}</strong> adet</span>
                  <span>Toplam Cihaz: {devices.length} adet</span>
                </div>
              </div>

              <div className="modal-footer">
                <button type="button" className="btn-secondary" onClick={() => setShowAssignDevicesModal(false)}>
                  İptal
                </button>
                <button
                  type="button"
                  className="btn-primary"
                  disabled={assigningDevices}
                  onClick={handleSaveDeviceAssignments}
                >
                  {assigningDevices ? "Kaydediliyor..." : `Kaydet ve Ata (${assignSelectedDeviceIds.size} Cihaz)`}
                </button>
              </div>
            </div>
          </div>
        )}

        {/* Modal: Şirket veya Departman Düzenle / Yeniden Adlandır */}
        {showEditGroupModal && editGroupTarget && (
          <div className="modal-backdrop" onClick={() => setShowEditGroupModal(false)}>
            <div className="modal-dialog" style={{ maxWidth: 440 }} onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div className="modal-title-with-icon">
                  <Building2 size={18} style={{ color: "var(--primary)" }} />
                  <div>
                    <h3 className="modal-title">
                      {editGroupTarget.parentGroupId ? "Departmanı Düzenle" : "Şirketi Düzenle"}
                    </h3>
                    <p className="modal-subtitle">Grup adını veya varsayılan politikasını güncelleyin.</p>
                  </div>
                </div>
                <button className="modal-close-btn" onClick={() => setShowEditGroupModal(false)}>
                  <X size={16} />
                </button>
              </div>
              <form onSubmit={handleSaveEditGroup}>
                <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
                  <div className="form-group">
                    <label className="form-label">{editGroupTarget.parentGroupId ? "Departman Adı *" : "Şirket Adı *"}</label>
                    <input
                      type="text"
                      className="form-input"
                      value={editGroupName}
                      onChange={(e) => setEditGroupName(e.target.value)}
                      required
                      autoFocus
                    />
                  </div>
                  <div className="form-group">
                    <label className="form-label">Varsayılan Ajan Güvenlik Politikası</label>
                    <select
                      className="form-input"
                      value={editGroupPolicyId}
                      onChange={(e) => setEditGroupPolicyId(e.target.value)}
                    >
                      <option value="">
                        {editGroupTarget.parentGroupId ? "— Miras Al (Şirket Politikası)" : "Standart / Kısıtlama Yok (Doğrudan Erişim)"}
                      </option>
                      {securityProfiles.map((p) => (
                        <option key={p.id} value={p.id}>
                          {p.name}{" "}
                          {p.consentMode === "always_prompt"
                            ? "(🟡 Her Zaman Onay)"
                            : p.consentMode === "prompt_if_active"
                            ? "(🔵 Aktifken Onay)"
                            : "(🟢 Doğrudan)"}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <div className="modal-footer">
                  <button type="button" className="btn-secondary" onClick={() => setShowEditGroupModal(false)}>
                    İptal
                  </button>
                  <button type="submit" className="btn-primary" disabled={savingGroup}>
                    {savingGroup ? "Kaydediliyor..." : "Değişiklikleri Kaydet"}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Modal: Canlı Güvenlik Profili Yapılandırıcı (Inline Security Profile Configurator) */}
        {showProfileConfigModal && (
          <div className="modal-backdrop" onClick={() => setShowProfileConfigModal(false)}>
            <div className="modal-dialog" style={{ maxWidth: 560, maxHeight: "90vh", display: "flex", flexDirection: "column" }} onClick={(e) => e.stopPropagation()}>
              <div className="modal-header">
                <div className="modal-title-with-icon">
                  <Lock size={18} style={{ color: "var(--primary)" }} />
                  <div>
                    <h3 className="modal-title">
                      {editingProfileId ? "Güvenlik Politikasını Yapılandır" : "Yeni Güvenlik Politikası Oluştur"}
                    </h3>
                    <p className="modal-subtitle">
                      {profileModalTarget?.name ? `Hedef: ${profileModalTarget.name}` : "Uzaktan bağlantı izin ve onay kuralları"}
                    </p>
                  </div>
                </div>
                <button className="modal-close-btn" onClick={() => setShowProfileConfigModal(false)}>
                  <X size={16} />
                </button>
              </div>

              <form onSubmit={handleSaveProfileModal} style={{ overflowY: "auto", flex: 1 }}>
                <div className="modal-body" style={{ display: "flex", flexDirection: "column", gap: "var(--space-3)" }}>
                  <div className="form-group">
                    <label className="form-label">Politika / Profil Adı *</label>
                    <input
                      type="text"
                      className="form-input"
                      placeholder="Örn: Muhasebe Onaylı Politika, Sunucu Doğrudan Bağlantı"
                      value={profileForm.name}
                      onChange={(e) => setProfileForm({ ...profileForm, name: e.target.value })}
                      required
                    />
                  </div>

                  {/* Onay Modu */}
                  <div className="form-group">
                    <label className="form-label">Bağlantı Onay Modu</label>
                    <select
                      className="form-input"
                      value={profileForm.consentMode}
                      onChange={(e) => setProfileForm({ ...profileForm, consentMode: e.target.value as any })}
                    >
                      <option value="unattended">🟢 Doğrudan Bağlan (Sormadan anında bağlan — IT &amp; Sunucu)</option>
                      <option value="always_prompt">🟡 Her Zaman Onay İste (Kullanıcı ekrandan kabul etmeden bağlanma — Muhasebe/Kullanıcı)</option>
                      <option value="prompt_if_active">🔵 Kullanıcı Aktifken Sor (Oturum açıkken sor, kilitliyken direkt bağlan)</option>
                    </select>
                  </div>

                  {profileForm.consentMode !== "unattended" && (
                    <div style={{ display: "grid", gridTemplateColumns: "1fr 1fr", gap: "var(--space-3)" }}>
                      <div className="form-group">
                        <label className="form-label">Onay Süresi (Saniye)</label>
                        <input
                          type="number"
                          className="form-input"
                          min={10}
                          max={300}
                          value={profileForm.consentTimeoutSeconds}
                          onChange={(e) => setProfileForm({ ...profileForm, consentTimeoutSeconds: Number(e.target.value) || 30 })}
                        />
                      </div>
                      <div className="form-group">
                        <label className="form-label">Zaman Aşımı Eylemi</label>
                        <select
                          className="form-input"
                          value={profileForm.consentDefaultAction}
                          onChange={(e) => setProfileForm({ ...profileForm, consentDefaultAction: e.target.value as any })}
                        >
                          <option value="deny">✖ Otomatik Reddet</option>
                          <option value="allow">✔ Otomatik Kabul Et</option>
                        </select>
                      </div>
                    </div>
                  )}

                  {/* İzinler */}
                  <div style={{ borderTop: "1px solid var(--border-subtle)", paddingTop: "var(--space-3)" }}>
                    <label className="form-label" style={{ marginBottom: 6 }}>Oturum İzinleri &amp; Kısıtlamalar</label>
                    <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={profileForm.viewOnlyMode}
                          onChange={(e) => setProfileForm({ ...profileForm, viewOnlyMode: e.target.checked })}
                        />
                        <span><strong>Sadece İzleme Modu</strong> (Fare ve klavye kontrolünü kapat)</span>
                      </label>
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={profileForm.allowRemoteTerminal}
                          onChange={(e) => setProfileForm({ ...profileForm, allowRemoteTerminal: e.target.checked })}
                        />
                        <span>Uzak Terminal / Komut Çalıştırmaya İzin Ver</span>
                      </label>
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={profileForm.allowClipboard}
                          onChange={(e) => setProfileForm({ ...profileForm, allowClipboard: e.target.checked })}
                        />
                        <span>Pano (Kopyala/Yapıştır) Senkronizasyonuna İzin Ver</span>
                      </label>
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={profileForm.allowFileTransfer}
                          onChange={(e) => setProfileForm({ ...profileForm, allowFileTransfer: e.target.checked })}
                        />
                        <span>Dosya Yükleme / İndirmeye İzin Ver</span>
                      </label>
                      <label className="remember-label">
                        <input
                          type="checkbox"
                          checked={profileForm.showConnectionBanner}
                          onChange={(e) => setProfileForm({ ...profileForm, showConnectionBanner: e.target.checked })}
                        />
                        <span>Ekranda 'Teknisyen Bağlı' Rozeti Göster</span>
                      </label>
                    </div>
                  </div>

                  {/* Parola Koruması */}
                  <div style={{ borderTop: "1px solid var(--border-subtle)", paddingTop: "var(--space-3)" }}>
                    <label className="remember-label" style={{ marginBottom: 6 }}>
                      <input
                        type="checkbox"
                        checked={profileForm.requirePassword}
                        onChange={(e) => setProfileForm({ ...profileForm, requirePassword: e.target.checked })}
                      />
                      <span><strong>Ajan Ayarları ve Çıkış İçin Şifre İste</strong></span>
                    </label>
                    {profileForm.requirePassword && (
                      <div className="form-group" style={{ marginTop: 6 }}>
                        <input
                          type="password"
                          className="form-input"
                          placeholder={editingProfileId ? "Mevcut şifreyi korumak için boş bırakın" : "Yönetici şifresi (en az 6 karakter)"}
                          value={profileForm.password}
                          onChange={(e) => setProfileForm({ ...profileForm, password: e.target.value })}
                          required={!editingProfileId}
                        />
                      </div>
                    )}
                  </div>
                </div>

                <div className="modal-footer">
                  <button type="button" className="btn-secondary" onClick={() => setShowProfileConfigModal(false)}>
                    İptal
                  </button>
                  <button type="submit" className="btn-primary" disabled={savingProfile}>
                    {savingProfile ? "Kaydediliyor..." : editingProfileId ? "Politikayı Güncelle" : "Politikayı Oluştur ve Ata"}
                  </button>
                </div>
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
