/**
 * Cihazın ağ bağdaştırıcısı detay tipi.
 */
export type NetworkAdapterInfo = {
  name: string;
  description: string;
  type: string;
  status: string;
  macAddress: string;
  ipAddresses: string[];
  gateways: string[];
  dnsServers: string[];
  speedMbps: number;
};

/**
 * Cihazda kurulu bir uygulamanın (yazılımın) detay tipi.
 */
export type InstalledAppInfo = {
  name: string;
  version?: string;
  publisher?: string;
  installDate?: string;
  estimatedSizeKb?: number;
  uninstallString?: string;
  quietUninstallString?: string;
};

export type WindowsUpdateInfo = {
  hotFixId: string;
  description?: string;
  installedOn?: string;
  installedBy?: string;
  supportUrl?: string;
  status?: string;
};

export type RamModuleInfo = {
  bankLabel: string;
  manufacturer?: string;
  partNumber?: string;
  serialNumber?: string;
  capacityMb: number;
  speedMhz?: number;
  memoryType?: string;
};

export type DiskDriveInfo = {
  model: string;
  serialNumber?: string;
  interfaceType?: string;
  mediaType?: string;
  sizeGb: number;
  partitionsCount?: number;
};

export type GpuInfo = {
  name: string;
  driverVersion?: string;
  vramMb?: number;
  videoProcessor?: string;
};

export type HardwareInventoryInfo = {
  systemSerialNumber?: string;
  systemManufacturer?: string;
  systemModel?: string;
  systemUuid?: string;
  biosSerialNumber?: string;
  biosVersion?: string;
  biosReleaseDate?: string;
  motherboardManufacturer?: string;
  motherboardProduct?: string;
  motherboardSerialNumber?: string;
  cpuName?: string;
  cpuProcessorId?: string;
  cpuCores?: number;
  cpuLogicalProcessors?: number;
  cpuMaxClockSpeedMhz?: number;
  ramModules?: RamModuleInfo[];
  diskDrives?: DiskDriveInfo[];
  graphicsCards?: GpuInfo[];
};

/**
 * REST API'den dönen zenginleştirilmiş istemci cihaz özeti ve canlı telemetrisi.
 */
export type DeviceSummary = {
  id: string;
  deviceName: string;
  domainName: string;
  operatingSystem: string;
  agentVersion: string;
  activeUser?: string;
  ipAddress?: string;
  locationCode?: string;
  cpuUsagePercent?: number;
  memoryTotalMb?: number;
  memoryUsedMb?: number;
  diskFreeMb?: number;
  uptimeSeconds?: number;
  isOnline: boolean;
  lastSeenAt: string;
  networkAdapters?: NetworkAdapterInfo[];
  installedApps?: InstalledAppInfo[];
  windowsUpdates?: WindowsUpdateInfo[];
  serialNumber?: string;
  hardwareDetails?: HardwareInventoryInfo;
  securityProfileId?: string | null;
  groupId?: string | null;
  profileId?: string | null;
  profileName?: string | null;
  hasCustomOverride?: boolean;
  appliedPolicyVersion?: number;
  lastPolicySyncedAt?: string | null;
};

/**
 * Teknisyen canlı oturum deep-link yanıt tipi.
 */
export type RemoteSession = {
  sessionId: string;
  deviceId: string;
  launchUri: string;
  expiresAt: string;
};

/**
 * Sunucuda barındırılan MSI kurulum paketi bilgileri.
 */
export type DownloadPackage = {
  id: string;
  name: string;
  description: string;
  fileName: string;
  url: string;
  language: string;
  requiresAdmin: boolean;
  exists: boolean;
  sizeBytes: number;
  version: string;
};

/**
 * Genel sunucu konfigürasyon ayarları tipi.
 */
export type ServerSettings = {
  serverUrl: string;
  enrollmentKey: string;
  heartbeatSeconds: number;
  defaultLocationCode: string;
  smtpHost?: string | null;
  smtpPort?: number;
  smtpUsername?: string | null;
  smtpPassword?: string | null;
  smtpFromAddress?: string | null;
  smtpFromName?: string | null;
  smtpSslMode?: string | null;
  alertsEnabled: boolean;
  alertRecipientEmails?: string | null;
  alertOfflineEnabled: boolean;
  alertOfflineMinutes: number;
  alertDiskLowEnabled: boolean;
  alertDiskLowMb: number;
  alertCpuHighEnabled: boolean;
  alertCpuHighPercent: number;
  alertMemoryHighEnabled: boolean;
  alertMemoryHighPercent: number;
};

/**
 * En son Agent ve Teknisyen sürüm ve OTA güncelleme sonucu.
 */
export type UpdateCheckResult = {
  agent: { version: string; downloadUrl: string; releaseNotes: string; sha256?: string; sizeBytes?: number };
  technician: { version: string; downloadUrl: string; releaseNotes: string; sha256?: string; sizeBytes?: number };
};

/**
 * Sunucudaki en güncel sürüm ve indirme URL'lerini sorgular.
 */
export async function checkUpdates(): Promise<UpdateCheckResult> {
  const response = await fetch("/api/updates/check");
  if (!response.ok) {
    throw new Error("Güncelleme bilgisi alınamadı.");
  }
  return response.json();
}

const LEGACY_TOKEN_STORAGE_KEY = "nexmote_admin_token";

export function clearStoredAdminToken(): void {
  localStorage.removeItem(LEGACY_TOKEN_STORAGE_KEY);
  sessionStorage.removeItem(LEGACY_TOKEN_STORAGE_KEY);
}

export function getStoredAdminToken(): string | null {
  clearStoredAdminToken();
  return null;
}

export function setStoredAdminToken(_token: string, _remember: boolean): void {
  clearStoredAdminToken();
}

function authHeaders(): Record<string, string> {
  return {
    "X-NexMote-Client": "Web"
  };
}

/** İki adımlı giriş akışının adım 1 (e-posta/şifre) yanıtı. */
export type LoginResult = {
  requiresMfa: boolean;
  token: string | null;
  challengeToken: string | null;
};

/** Giriş yapmış kullanıcının kimlik/rol bilgisi (/api/auth/me). */
export type CurrentUser = {
  id: string;
  email: string;
  displayName: string;
  role: "Admin" | "Technician";
  mfaEnabled: boolean;
};

export type UserSummary = {
  id: string;
  email: string;
  displayName: string;
  role: "Admin" | "Technician";
  isActive: boolean;
  mfaEnabled: boolean;
  createdAt: string;
  lastLoginAt: string | null;
};

export type ActivityLogEntry = {
  id: string;
  userId: string | null;
  userEmail: string | null;
  action: string;
  targetType: string | null;
  targetId: string | null;
  detailsJson: string | null;
  ipAddress: string | null;
  success: boolean;
  createdAt: string;
  correlationId?: string | null;
};

/**
 * Giriş adım 1: e-posta ve şifreyi doğrular (/api/auth/login). MFA kapalıysa doğrudan oturum
 * token'ı, açıksa bir MFA challenge token'ı döner — asıl oturum için verifyMfa() çağrılmalıdır.
 */
export async function login(email: string, password: string, rememberMe = false): Promise<LoginResult> {
  const response = await fetch(`/api/auth/login?rememberMe=${rememberMe}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password })
  });

  if (!response.ok) {
    throw new Error("Hatalı e-posta veya parola.");
  }

  return response.json();
}

/**
 * Giriş adım 2: MFA challenge token'ı + authenticator kodunu (veya kurtarma kodunu) doğrular, oturum token'ı döner.
 */
export async function verifyMfa(challengeToken: string, code: string, rememberMe = false): Promise<LoginResult> {
  const response = await fetch(`/api/auth/mfa/verify?rememberMe=${rememberMe}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ challengeToken, code })
  });

  if (!response.ok) {
    throw new Error("Kod hatalı veya süresi dolmuş.");
  }

  return response.json();
}

/** Mevcut oturumu sunucuda iptal eder. */
export async function logout(): Promise<void> {
  await fetch("/api/auth/logout", { method: "POST", headers: authHeaders() }).catch(() => {});
}

/** Giriş yapmış kullanıcının kimlik/rol bilgisini döner. */
export async function getCurrentUser(): Promise<CurrentUser> {
  const response = await fetch("/api/auth/me", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Kullanıcı bilgisi alınamadı.");
  }
  return response.json();
}

/** Kendi şifresini değiştirir. */
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const response = await fetch("/api/account/password", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ currentPassword, newPassword })
  });
  if (!response.ok) {
    throw new Error("Mevcut şifre hatalı.");
  }
}

/** MFA kurulumunu başlatır — QR'nin üretileceği otpauth:// URI ve secret döner. */
export async function setupMfa(): Promise<{ secret: string; provisioningUri: string }> {
  const response = await fetch("/api/account/mfa/setup", { method: "POST", headers: authHeaders() });
  if (!response.ok) {
    throw new Error("MFA kurulumu başlatılamadı.");
  }
  return response.json();
}

/** MFA kurulumunu ilk 6 haneli kodla onaylar, kurtarma kodlarını bir kereliğine döner. */
export async function enableMfa(code: string): Promise<{ recoveryCodes: string[] }> {
  const response = await fetch("/api/account/mfa/enable", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ code })
  });
  if (!response.ok) {
    throw new Error("Kod doğrulanamadı.");
  }
  return response.json();
}

/** MFA'yı kapatır (mevcut şifre doğrulaması gerektirir). */
export async function disableMfa(currentPassword: string): Promise<void> {
  const response = await fetch("/api/account/mfa/disable", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ currentPassword })
  });
  if (!response.ok) {
    throw new Error("Şifre hatalı.");
  }
}

/** Kullanıcı listesini döner (Admin). */
export async function listUsers(): Promise<UserSummary[]> {
  const response = await fetch("/api/admin/users", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Kullanıcı listesi alınamadı.");
  }
  return response.json();
}

/** Yeni Admin veya Teknisyen hesabı oluşturur, tek seferlik geçici şifre döner (Admin). */
export async function createUser(email: string, displayName: string, role: "Admin" | "Technician"): Promise<{ id: string; email: string; temporaryPassword: string }> {
  const response = await fetch("/api/admin/users", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ email, displayName, role })
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Kullanıcı oluşturulamadı.");
  }
  return response.json();
}

/** Yeni kullanıcıyı e-posta ile davet eder — geçici şifre yerine bir davet linki gönderir (Admin). */
export async function inviteUser(email: string, displayName: string, role: "Admin" | "Technician"): Promise<{ message: string; email: string }> {
  const response = await fetch("/api/admin/users/invite", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ email, displayName, role })
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Davet gönderilemedi.");
  }
  return response.json();
}

export type SmtpTestOptions = {
  toEmail: string;
  host?: string | null;
  port?: number;
  username?: string | null;
  password?: string | null;
  fromAddress?: string | null;
  fromName?: string | null;
  sslMode?: string | null;
};

/** Kayıtlı veya formdaki SMTP ayarlarıyla verilen adrese test e-postası gönderir (Admin). */
export async function testSmtp(options: string | SmtpTestOptions): Promise<{ message: string }> {
  const payload = typeof options === "string" ? { toEmail: options } : options;
  const response = await fetch("/api/admin/settings/smtp/test", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(payload)
  });
  const detail = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(detail?.message ?? "Test e-postası gönderilemedi.");
  }
  return detail;
}

/** Davet önizlemesini getirir (public — davet kabul ekranı için). */
export async function getInvitePreview(token: string): Promise<{ email: string; displayName: string; role: "Admin" | "Technician" }> {
  const response = await fetch(`/api/invite/${token}`);
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Davet geçersiz veya süresi dolmuş.");
  }
  return response.json();
}

/** Daveti kabul eder — şifre belirler, hesabı etkinleştirir, oturum token'ı döner (public). */
export async function acceptInvite(token: string, password: string): Promise<LoginResult> {
  const response = await fetch(`/api/invite/${token}/accept`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ password })
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Davet kabul edilemedi.");
  }
  return response.json();
}

/** Kullanıcının rolünü değiştirir (Admin). */
export async function setUserRole(userId: string, role: "Admin" | "Technician"): Promise<void> {
  const response = await fetch(`/api/admin/users/${userId}/role`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ role })
  });
  if (!response.ok) {
    throw new Error("Rol değiştirilemedi.");
  }
}

/** Kullanıcı hesabını devre dışı bırakır (Admin). */
export async function disableUser(userId: string): Promise<void> {
  const response = await fetch(`/api/admin/users/${userId}/disable`, { method: "POST", headers: authHeaders() });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Kullanıcı devre dışı bırakılamadı.");
  }
}

/** Devre dışı bırakılmış kullanıcı hesabını yeniden etkinleştirir (Admin). */
export async function enableUser(userId: string): Promise<void> {
  const response = await fetch(`/api/admin/users/${userId}/enable`, { method: "POST", headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Kullanıcı etkinleştirilemedi.");
  }
}

/** Bir kullanıcının şifresini yönetici olarak doğrudan değiştirir (Admin). */
export async function adminChangeUserPassword(userId: string, newPassword: string): Promise<{ message: string }> {
  const response = await fetch(`/api/admin/users/${userId}/password`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ newPassword })
  });
  const detail = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(detail?.message ?? "Şifre değiştirilemedi.");
  }
  return detail;
}

/** Kullanıcı hesabını sistemden tamamen siler (Admin). */
export async function deleteUser(userId: string): Promise<{ message: string }> {
  const response = await fetch(`/api/admin/users/${userId}`, {
    method: "DELETE",
    headers: authHeaders()
  });
  const detail = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(detail?.message ?? "Kullanıcı silinemedi.");
  }
  return detail;
}

/** Kilitlenmiş veya MFA açık bir kullanıcının MFA'sını admin sıfırlar/kaldırır. */
export async function resetUserMfa(userId: string): Promise<{ message: string }> {
  const response = await fetch(`/api/admin/users/${userId}/mfa/reset`, { method: "POST", headers: authHeaders() });
  const detail = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(detail?.message ?? "MFA sıfırlanamadı.");
  }
  return detail;
}

/** Sayfalanmış, filtrelenebilir denetim (activity) logu (Admin). */
export async function getAuditLog(page = 1, pageSize = 50, userId?: string): Promise<{ items: ActivityLogEntry[]; total: number }> {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (userId) params.set("userId", userId);
  const response = await fetch(`/api/admin/audit-log?${params.toString()}`, { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Denetim logu alınamadı.");
  }
  return response.json();
}

/**
 * Kayıtlı cihazların ve donanım metriklerinin listesini sunucudan çeker.
 */
export async function listDevices(): Promise<DeviceSummary[]> {
  const response = await fetch("/api/devices", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Cihaz listesi alınamadı.");
  }
  return response.json();
}

export interface DeviceQueryOptions {
  page?: number;
  pageSize?: number;
  search?: string;
  status?: "all" | "online" | "offline";
  groupId?: string;
  sortBy?: "name" | "lastSeen" | "cpu" | "memory" | "os" | "user" | "uptime";
  sortDir?: "asc" | "desc";
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  onlineCount: number;
  offlineCount: number;
}

/**
 * Sunucu taraflı arama, filtreleme, sıralama ve sayfalama ile cihaz listesini çeker (Madde 7).
 */
export async function listDevicesPaged(options: DeviceQueryOptions = {}): Promise<PagedResult<DeviceSummary>> {
  const params = new URLSearchParams();
  if (options.page) params.set("page", options.page.toString());
  if (options.pageSize) params.set("pageSize", options.pageSize.toString());
  if (options.search) params.set("search", options.search);
  if (options.status) params.set("status", options.status);
  if (options.groupId) params.set("groupId", options.groupId);
  if (options.sortBy) params.set("sortBy", options.sortBy);
  if (options.sortDir) params.set("sortDir", options.sortDir);

  const qs = params.toString();
  const url = qs ? `/api/devices/paged?${qs}` : "/api/devices/paged";
  const response = await fetch(url, { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Sayfalanmış cihaz listesi alınamadı.");
  }
  return response.json();
}

/**
 * Kayıtlı bir cihazı sistemden siler.
 * @param uninstallAgent Eğer true ise hedef bilgisayara uzaktan sessiz ajan kaldırma emri gönderilir.
 */
export async function deleteDevice(deviceId: string, uninstallAgent = true): Promise<void> {
  const response = await fetch(`/api/devices/${deviceId}?uninstallAgent=${uninstallAgent}`, {
    method: "DELETE",
    headers: authHeaders()
  });

  if (!response.ok && response.status !== 204 && response.status !== 404) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Cihaz silinemedi.");
  }
}

/**
 * Hedef cihaz için canlı uzaktan bağlantı oturumu (nexmote://) oluşturur.
 */
export async function createRemoteSession(deviceId: string): Promise<RemoteSession> {
  const response = await fetch("/api/remote-sessions", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ deviceId })
  });

  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Bağlantı oturumu oluşturulamadı.");
  }

  return response.json();
}

/**
 * Sunucudaki indirilebilir paket kataloğunu çeker.
 */
export async function listDownloads(): Promise<DownloadPackage[]> {
  const response = await fetch("/api/downloads");
  if (!response.ok) {
    throw new Error("İndirme kataloğu alınamadı.");
  }
  return response.json();
}

/**
 * Sunucu genel yapılandırma ayarlarını okur.
 */
export async function getServerSettings(): Promise<ServerSettings> {
  const response = await fetch("/api/settings", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Sunucu ayarları alınamadı.");
  }
  return response.json();
}

/**
 * Sunucu genel yapılandırma ayarlarını günceller.
 */
export async function updateServerSettings(settings: ServerSettings): Promise<ServerSettings> {
  const response = await fetch("/api/settings", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(settings)
  });

  if (!response.ok) {
    throw new Error("Ayarlar kaydedilemedi.");
  }

  return response.json();
}

/**
 * Sunucu canlı performans ve donanım metrikleri tipi.
 */
export type ServerMetrics = {
  cpuUsagePercent: number;
  memoryTotalMb: number;
  memoryUsedMb: number;
  memoryFreeMb: number;
  memoryUsagePercent: number;
  diskTotalGb: number;
  diskUsedGb: number;
  diskFreeGb: number;
  diskUsagePercent: number;
  networkInMbps: number;
  networkOutMbps: number;
  totalRxMb: number;
  totalTxMb: number;
  uptimeSeconds: number;
  osDescription: string;
  measuredAt: string;
};

/**
 * Sunucu anlık performans metriklerini (CPU, RAM, Disk, Ağ Bant Genişliği) okur.
 */
export async function getServerMetrics(): Promise<ServerMetrics> {
  const response = await fetch("/api/server/metrics", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Sunucu performans metrikleri alınamadı.");
  }
  return response.json();
}

/**
 * Seçili cihaza uzaktan sessiz Agent güncelleme sinyali gönderir.
 */
export async function triggerAgentUpdate(deviceId: string): Promise<{ message: string }> {
  const response = await fetch(`/api/agents/${deviceId}/update`, {
    method: "POST",
    headers: authHeaders()
  });

  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Agent güncelleme sinyali gönderilemedi.");
  }

  return response.json();
}

/**
 * Web terminal komut çalıştırma yanıt tipi.
 */
export type CommandExecutionResponse = {
  requestId: string;
  shell: string;
  command: string;
  queued?: boolean;
  exitCode: number;
  stdOut: string;
  stdErr: string;
  durationMs: number;
  timedOut: boolean;
  elevationDenied: boolean;
};

/**
 * Cihaz üzerinde doğrudan CMD veya PowerShell komutu çalıştırır (cihaza canlı bağlanmadan).
 */
export async function executeDeviceCommand(
  deviceId: string,
  shell: "cmd" | "powershell",
  command: string,
  runAsAdmin = true,
  timeoutSeconds = 30
): Promise<CommandExecutionResponse> {
  const res = await fetch(`/api/devices/${deviceId}/execute-command`, {
    method: "POST",
    headers: {
      ...authHeaders(),
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ shell, command, runAsAdmin, timeoutSeconds })
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ message: "Komut çalıştırılamadı." }));
    throw new Error(err.message || `Sunucu hatası: ${res.status}`);
  }

  return res.json();
}

export async function uninstallApp(
  deviceId: string,
  app: { appName: string; uninstallString?: string; quietUninstallString?: string }
): Promise<{ success: boolean; queued?: boolean; appName: string; exitCode: number; stdOut?: string; stdErr?: string; message: string }> {
  const res = await fetch(`/api/devices/${deviceId}/uninstall-app`, {
    method: "POST",
    headers: {
      ...authHeaders(),
      "Content-Type": "application/json"
    },
    body: JSON.stringify({
      appName: app.appName,
      uninstallString: app.uninstallString,
      quietUninstallString: app.quietUninstallString
    })
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ message: "Uygulama kaldırma isteği başarısız oldu." }));
    throw new Error(err.message || `Sunucu hatası: ${res.status}`);
  }

  return res.json();
}

/** Kurumsal ajan güvenlik profili — branding, kısıtlı tray menüsü, bağlantı onay politikaları ve izinler. */
export type SecurityProfile = {
  id: string;
  name: string;
  agentDisplayName?: string | null;
  iconBase64?: string | null;
  restrictTrayMenu: boolean;
  requirePassword: boolean;
  consentMode: "unattended" | "always_prompt" | "prompt_if_active";
  consentTimeoutSeconds: number;
  consentDefaultAction: "deny" | "allow";
  viewOnlyMode: boolean;
  allowRemoteTerminal: boolean;
  allowClipboard: boolean;
  allowFileTransfer: boolean;
  showConnectionBanner: boolean;
  createdAt: string;
  updatedAt: string;
};

export type SecurityProfileInput = {
  name: string;
  agentDisplayName?: string;
  iconBase64?: string;
  restrictTrayMenu: boolean;
  requirePassword: boolean;
  password?: string;
  consentMode: "unattended" | "always_prompt" | "prompt_if_active";
  consentTimeoutSeconds: number;
  consentDefaultAction: "deny" | "allow";
  viewOnlyMode: boolean;
  allowRemoteTerminal: boolean;
  allowClipboard: boolean;
  allowFileTransfer: boolean;
  showConnectionBanner: boolean;
};

/** Güvenlik profillerini listeler (Admin). */
export async function listSecurityProfiles(): Promise<SecurityProfile[]> {
  const response = await fetch("/api/admin/security-profiles", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Güvenlik profilleri alınamadı.");
  }
  return response.json();
}

/** Yeni güvenlik profili oluşturur (Admin). */
export async function createSecurityProfile(input: SecurityProfileInput): Promise<SecurityProfile> {
  const response = await fetch("/api/admin/security-profiles", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(input)
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Profil oluşturulamadı.");
  }
  return response.json();
}

/** Güvenlik profilini günceller (Admin). */
export async function updateSecurityProfile(id: string, input: SecurityProfileInput): Promise<SecurityProfile> {
  const response = await fetch(`/api/admin/security-profiles/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(input)
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Profil güncellenemedi.");
  }
  return response.json();
}

/** Güvenlik profilini siler (Admin). */
export async function deleteSecurityProfile(id: string): Promise<void> {
  const response = await fetch(`/api/admin/security-profiles/${id}`, { method: "DELETE", headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Profil silinemedi.");
  }
}

/** Bir cihaza güvenlik profili atar (null = kaldır) (Admin). */
export async function assignSecurityProfile(deviceId: string, securityProfileId: string | null): Promise<void> {
  const response = await fetch(`/api/devices/${deviceId}/security-profile`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ securityProfileId })
  });
  if (!response.ok) {
    throw new Error("Güvenlik profili atanamadı.");
  }
}

/** Cihazları organize etmek için iç içe (şirket/departman) grup. */
export type DeviceGroup = {
  id: string;
  name: string;
  parentGroupId?: string | null;
  defaultSecurityProfileId?: string | null;
  enrollmentKey?: string | null;
  createdAt: string;
};

export type DeviceGroupInput = {
  name: string;
  parentGroupId?: string | null;
  defaultSecurityProfileId?: string | null;
};

/** Cihaz gruplarını listeler (Admin). */
export async function listDeviceGroups(): Promise<DeviceGroup[]> {
  const response = await fetch("/api/admin/device-groups", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Cihaz grupları alınamadı.");
  }
  return response.json();
}

/** Yeni cihaz grubu oluşturur (Admin). */
export async function createDeviceGroup(input: DeviceGroupInput): Promise<DeviceGroup> {
  const response = await fetch("/api/admin/device-groups", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(input)
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Grup oluşturulamadı.");
  }
  return response.json();
}

/** Cihaz grubunu günceller (Admin). */
export async function updateDeviceGroup(id: string, input: DeviceGroupInput): Promise<DeviceGroup> {
  const response = await fetch(`/api/admin/device-groups/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(input)
  });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Grup güncellenemedi.");
  }
  return response.json();
}

/** Cihaz grubunu siler (Admin). */
export async function deleteDeviceGroup(id: string): Promise<void> {
  const response = await fetch(`/api/admin/device-groups/${id}`, { method: "DELETE", headers: authHeaders() });
  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Grup silinemedi.");
  }
}

/** Bir grubun kurulum anahtarını yeniden üretir (Admin). Eski anahtarla üretilmiş provizyon script'leri artık bu gruba düşmez. */
export async function regenerateDeviceGroupEnrollmentKey(id: string): Promise<DeviceGroup> {
  const response = await fetch(`/api/admin/device-groups/${id}/enrollment-key/regenerate`, {
    method: "POST",
    headers: authHeaders()
  });
  if (!response.ok) {
    throw new Error("Kurulum anahtarı yeniden oluşturulamadı.");
  }
  return response.json();
}

/** Bu gruba özel provizyon script'ini (.ps1) indirir — kurulumdan hemen sonra çalıştırılınca ajanı otomatik olarak bu gruba/profile bağlar (Admin). */
export async function downloadDeviceGroupProvisionScript(id: string, groupName: string): Promise<void> {
  const response = await fetch(`/api/admin/device-groups/${id}/provision-script?serverUrl=${encodeURIComponent(window.location.origin)}`, {
    headers: authHeaders()
  });
  if (!response.ok) {
    throw new Error("Provizyon script'i indirilemedi.");
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `NexMote-Provision-${groupName.replace(/[^a-zA-Z0-9-_]+/g, "")}.ps1`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Bu gruba özel TEK kurulum script'ini (.ps1) indirir — MSI'ı indirip sessizce kurar ve ajanı doğrudan bu gruba/profile bağlar, ayrı bir provizyon adımı gerekmez (Admin). */
export async function downloadDeviceGroupInstallScript(id: string, groupName: string): Promise<void> {
  const response = await fetch(`/api/admin/device-groups/${id}/install-script?serverUrl=${encodeURIComponent(window.location.origin)}`, {
    headers: authHeaders()
  });
  if (!response.ok) {
    throw new Error("Kurulum script'i indirilemedi.");
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `NexMote-Install-${groupName.replace(/[^a-zA-Z0-9-_]+/g, "")}.ps1`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** Bir cihazı bir gruba atar (null = kaldır) (Admin). */
export async function assignDeviceGroup(deviceId: string, groupId: string | null): Promise<void> {
  const response = await fetch(`/api/devices/${deviceId}/group`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ groupId })
  });
  if (!response.ok) {
    throw new Error("Grup atanamadı.");
  }
}

/** Şu an açık (çözülmemiş) bir cihaz uyarısı — çevrimdışı, disk/CPU/RAM eşik aşımı. */
export type ActiveDeviceAlert = {
  deviceId: string;
  alertType: "Offline" | "DiskLow" | "CpuHigh" | "MemoryHigh";
  triggeredAt: string;
};

/** Şu an açık olan tüm cihaz uyarılarını listeler. */
export async function getActiveAlerts(): Promise<ActiveDeviceAlert[]> {
  const response = await fetch("/api/alerts/active", { headers: authHeaders() });
  if (!response.ok) {
    throw new Error("Aktif uyarılar alınamadı.");
  }
  return response.json();
}

/** Desteklenen uzaktan güç eylemleri tipi. */
export type PowerActionType = "reboot" | "shutdown" | "lock" | "logoff" | "reboot-safe" | "reboot-normal";

export type DevicePowerResult = {
  success: boolean;
  action: string;
  message: string;
};

/**
 * Hedef cihaza uzaktan güç komutu (yeniden başlat, kapat, kilitle, oturumu kapat vb.) iletir.
 */
export async function sendDevicePowerAction(deviceId: string, action: PowerActionType): Promise<DevicePowerResult> {
  const response = await fetch(`/api/devices/${deviceId}/power`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ action })
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    throw new Error(data.message || "Güç eylemi iletilemedi.");
  }
  return data;
}

// =========================================================================
// HİYERARŞİK PROFİL & POLİTİKA YÖNETİMİ API
// =========================================================================

export interface PolicyBranding {
  companyName?: string | null;
  agentDisplayName?: string | null;
  logoBase64?: string | null;
  trayIconBase64?: string | null;
  aboutText?: string | null;
  supportContact?: string | null;
}

export interface PolicyProtection {
  agentProtection?: boolean | null;
  protectionPasswordHash?: string | null;
  allowAgentExit?: boolean | null;
  allowServiceStop?: boolean | null;
  allowAgentUninstall?: boolean | null;
}

export interface PolicyRemoteAccess {
  mode?: string | null; // "unattended" | "prompt" | "auto_accept_idle"
  promptTimeoutSeconds?: number | null;
  defaultAction?: string | null; // "deny" | "allow"
  idleTimeoutMinutes?: number | null;
  showConnectionBanner?: boolean | null;
  viewOnlyMode?: boolean | null;
  allowRemoteTerminal?: boolean | null;
  allowClipboard?: boolean | null;
  allowFileTransfer?: boolean | null;
}

export interface UsbDeviceItem {
  id: string;
  name: string;
  hardwareId?: string | null;
  vendorId?: string | null;
  productId?: string | null;
  serialNumber?: string | null;
}

export interface PolicyUsb {
  mode?: string | null; // "allow_all" | "block_all" | "block_storage" | "read_only" | "whitelist"
  whitelist?: UsbDeviceItem[] | null;
}

export interface PolicyDocument {
  version: number;
  profileId?: string | null;
  profileName?: string | null;
  parentProfileId?: string | null;
  branding?: PolicyBranding;
  protection?: PolicyProtection;
  remoteAccess?: PolicyRemoteAccess;
  usb?: PolicyUsb;
  customModules?: Record<string, string>;
}

export interface ProfileTreeNode {
  id: string;
  name: string;
  parentProfileId?: string | null;
  type: string; // "Company" | "Department" | "Location" | "Custom"
  policyVersion: number;
  deviceCount: number;
  subProfileCount: number;
  companyName?: string | null;
  updatedAt: string;
  children: ProfileTreeNode[];
}

export interface ProfileDetailResponse {
  id: string;
  name: string;
  parentProfileId?: string | null;
  type: string;
  policyVersion: number;
  enrollmentKey?: string | null;
  ownPolicy: PolicyDocument;
  effectivePolicy: PolicyDocument;
  deviceCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ProfileUpsertRequest {
  name: string;
  parentProfileId?: string | null;
  type: string;
  policy: PolicyDocument;
  newProtectionPassword?: string | null;
}

export interface DevicePolicyOverrideRequest {
  hasCustomOverride: boolean;
  customPolicy?: PolicyDocument | null;
}

export interface BulkAssignProfileRequest {
  deviceIds: string[];
  targetProfileId?: string | null;
}

/** Hiyerarşik profil ağacını döner (Şirket > Departman > Lokasyon). */
export async function getProfileTree(): Promise<ProfileTreeNode[]> {
  const response = await fetch("/api/profiles/tree", { headers: authHeaders() });
  if (!response.ok) throw new Error("Profil ağacı yüklenemedi.");
  return response.json();
}

/** Belirli bir profilin detayını ve efektif politikasını döner. */
export async function getProfile(id: string): Promise<ProfileDetailResponse> {
  const response = await fetch(`/api/profiles/${id}`, { headers: authHeaders() });
  if (!response.ok) throw new Error("Profil detayı yüklenemedi.");
  return response.json();
}

/** Yeni bir kurumsal profil oluşturur. */
export async function createProfile(req: ProfileUpsertRequest): Promise<ProfileDetailResponse> {
  const response = await fetch("/api/admin/profiles", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(req)
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || "Profil oluşturulamadı.");
  return data;
}

/** Mevcut bir profili ve politikasını günceller. */
export async function updateProfile(id: string, req: ProfileUpsertRequest): Promise<ProfileDetailResponse> {
  const response = await fetch(`/api/admin/profiles/${id}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(req)
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || "Profil güncellenemedi.");
  return data;
}

/** Bir profili siler (alt profili veya bağlı cihazı varsa uyarı verir). */
export async function deleteProfile(id: string): Promise<void> {
  const response = await fetch(`/api/admin/profiles/${id}`, {
    method: "DELETE",
    headers: authHeaders()
  });
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.message || "Profil silinemedi.");
  }
}

/** Bir profili alt politikalarıyla birlikte kopyalar/klonlar. */
export async function cloneProfile(id: string, newName?: string): Promise<ProfileDetailResponse> {
  const url = newName ? `/api/admin/profiles/${id}/clone?newName=${encodeURIComponent(newName)}` : `/api/admin/profiles/${id}/clone`;
  const response = await fetch(url, {
    method: "POST",
    headers: authHeaders()
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || "Profil kopyalanamadı.");
  return data;
}

/** "Politikayı Şimdi Uygula": Bu profile bağlı tüm cihazlara anlık SignalR zorlama sinyali gönderir. */
export async function applyProfilePolicyNow(id: string): Promise<{ message: string }> {
  const response = await fetch(`/api/admin/profiles/${id}/apply-now`, {
    method: "POST",
    headers: authHeaders()
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || "Politika anlık uygulama sinyali gönderilemedi.");
  return data;
}

/** Çoklu cihazı seçilen bir profile toplu atar. */
export async function bulkAssignProfile(req: BulkAssignProfileRequest): Promise<{ count: number }> {
  const response = await fetch("/api/admin/devices/bulk-assign-profile", {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(req)
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(data.message || "Toplu profil atama başarısız.");
  return data;
}

/** Tekil bir cihazı profile atar veya profilden çıkarır. */
export async function assignDeviceProfile(deviceId: string, profileId: string | null): Promise<void> {
  const response = await fetch(`/api/devices/${deviceId}/profile`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(profileId)
  });
  if (!response.ok) throw new Error("Cihaz profili güncellenemedi.");
}

/** Cihaza özel override politikası belirler veya kaldırır. */
export async function setDevicePolicyOverride(deviceId: string, req: DevicePolicyOverrideRequest): Promise<void> {
  const response = await fetch(`/api/devices/${deviceId}/override-policy`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify(req)
  });
  if (!response.ok) throw new Error("Cihaz özel politikası güncellenemedi.");
}

/** Cihazın hiyerarşik veya özel etkin (effective) politikasını döner. */
export async function getDeviceEffectivePolicy(deviceId: string): Promise<PolicyDocument> {
  const response = await fetch(`/api/devices/${deviceId}/effective-policy`, { headers: authHeaders() });
  if (!response.ok) throw new Error("Cihaz etkin politikası alınamadı.");
  return response.json();
}
