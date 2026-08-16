/**
 * Sunucudan dönen kayıtlı cihaz özet ve canlı donanım metrikleri tipi.
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
 * Tarayıcı depolama alanından (localStorage veya sessionStorage) admin token'ını okur.
 */
export function getStoredAdminToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY) ?? sessionStorage.getItem(TOKEN_STORAGE_KEY);
}

/**
 * Admin giriş token'ını tarayıcıya kaydeder (Beni hatırla seçeneğine göre).
 */
export function setStoredAdminToken(token: string, remember: boolean): void {
  if (remember) {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
  } else {
    sessionStorage.setItem(TOKEN_STORAGE_KEY, token);
  }
}

/**
 * Kayıtlı admin oturum token'ını temizler.
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

/**
 * Admin e-posta ve şifresiyle sunucuya giriş yapar (/api/auth/login).
 */
export async function login(email: string, password: string): Promise<string> {
  const response = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ email, password })
  });

  if (!response.ok) {
    throw new Error("Hatalı e-posta veya parola.");
  }

  const data: { token: string } = await response.json();
  return data.token;
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
