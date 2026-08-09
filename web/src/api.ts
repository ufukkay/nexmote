export type DeviceSummary = {
  id: string;
  deviceName: string;
  domainName: string;
  operatingSystem: string;
  agentVersion: string;
  activeUser?: string;
  ipAddress?: string;
  locationCode?: string;
  isOnline: boolean;
  lastSeenAt: string;
};

export type RemoteSession = {
  sessionId: string;
  deviceId: string;
  launchUri: string;
  expiresAt: string;
};

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

export type ServerSettings = {
  serverUrl: string;
  enrollmentKey: string;
  heartbeatSeconds: number;
  defaultLocationCode: string;
};

export async function listDevices(): Promise<DeviceSummary[]> {
  const response = await fetch("/api/devices");
  if (!response.ok) {
    throw new Error("Cihaz listesi alınamadı.");
  }
  return response.json();
}

export async function createRemoteSession(deviceId: string): Promise<RemoteSession> {
  const response = await fetch("/api/remote-sessions", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ deviceId })
  });

  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.message ?? "Bağlantı oturumu oluşturulamadı.");
  }

  return response.json();
}

export async function listDownloads(): Promise<DownloadPackage[]> {
  const response = await fetch("/api/downloads");
  if (!response.ok) {
    throw new Error("İndirme kataloğu alınamadı.");
  }
  return response.json();
}

export async function getServerSettings(): Promise<ServerSettings> {
  const response = await fetch("/api/settings");
  if (!response.ok) {
    throw new Error("Sunucu ayarları alınamadı.");
  }
  return response.json();
}

export async function updateServerSettings(settings: ServerSettings): Promise<ServerSettings> {
  const response = await fetch("/api/settings", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(settings)
  });

  if (!response.ok) {
    throw new Error("Ayarlar kaydedilemedi.");
  }

  return response.json();
}

export async function generatePackages(settings: ServerSettings): Promise<{ message: string; serverUrl: string }> {
  const response = await fetch("/api/downloads/generate", {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(settings)
  });

  if (!response.ok) {
    const detail = await response.json().catch(() => null);
    throw new Error(detail?.detail ?? "Agent paketleri oluşturulamadı.");
  }

  return response.json();
}
