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
  isOnline: boolean;
  lastSeenAt: string;
  networkAdapters?: NetworkAdapterInfo[];
  installedApps?: InstalledAppInfo[];
  windowsUpdates?: WindowsUpdateInfo[];
  serialNumber?: string;
  hardwareDetails?: HardwareInventoryInfo;
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
};

/**
 * En son Agent ve Teknisyen sürüm ve OTA güncelleme sonucu.
 */
export type UpdateCheckResult = {
  agent: { version: string; downloadUrl: string; releaseNotes: string };
  technician: { version: string; downloadUrl: string; releaseNotes: string };
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

const TOKEN_STORAGE_KEY = "nexmote_admin_token";

/**
 * Güvenlik notu:
 *  - "Beni hatırla" seçiliyse token localStorage'a yazılır (sekme kapatılsa da kalır).
 *  - Seçili değilse yalnızca sessionStorage kullanılır (sekme kapanınca silinir).
 *  - Her iki durumda da token yalnızca aynı origin'den (same-site) okunabilir.
 *  - XSS riskini minimize etmek için hiçbir harici script yüklenmiyor,
 *    dangerouslySetInnerHTML ve eval() kullanılmıyor (bkz. App.tsx).
 */

/**
 * Tarayıcı depolama alanından (önce sessionStorage, sonra localStorage) admin token'ını okur.
 * sessionStorage önceliklidir: aktif sekme oturumu daha kısa ömürlü olduğundan daha güvenlidir.
 */
export function getStoredAdminToken(): string | null {
  // Önce session (kısa ömürlü, daha güvenli)
  const sessionToken = sessionStorage.getItem(TOKEN_STORAGE_KEY);
  if (sessionToken) return sessionToken;
  // Yoksa "beni hatırla" ile kaydedilmiş kalıcı token
  return localStorage.getItem(TOKEN_STORAGE_KEY);
}

/**
 * Admin giriş token'ını tarayıcıya kaydeder.
 * remember=true  → localStorage  (sekme kapansa da kalır)
 * remember=false → sessionStorage (sekme/tarayıcı kapanınca silinir — varsayılan daha güvenli)
 */
export function setStoredAdminToken(token: string, remember: boolean): void {
  if (remember) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
    // sessionStorage'daki eski kopyayı temizle
    sessionStorage.removeItem(TOKEN_STORAGE_KEY);
  } else {
    sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
    // localStorage'daki eski kopyayı temizle
    localStorage.removeItem(TOKEN_STORAGE_KEY);
  }
}

/**
 * Kayıtlı admin oturum token'ını her iki depolama alanından da temizler.
 */
export function clearStoredAdminToken(): void {
  localStorage.removeItem(TOKEN_STORAGE_KEY);
  sessionStorage.removeItem(TOKEN_STORAGE_KEY);
}

/**
 * Korumalı API istekleri için Bearer Authorization başlığını üretir.
 */
function authHeaders(): Record<string, string> {
  const token = getStoredAdminToken();
  return token ? { Authorization: `Bearer ${token}` } : {};
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

/** Kilitlenmiş bir kullanıcının MFA'sını admin zorla kapatır. */
export async function resetUserMfa(userId: string): Promise<void> {
  const response = await fetch(`/api/admin/users/${userId}/mfa/reset`, { method: "POST", headers: authHeaders() });
  if (!response.ok) {
    throw new Error("MFA sıfırlanamadı.");
  }
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
): Promise<{ success: boolean; appName: string; exitCode: number; stdOut?: string; stdErr?: string; message: string }> {
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

