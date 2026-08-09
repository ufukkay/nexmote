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

export async function listDevices(): Promise<DeviceSummary[]> {
  const response = await fetch("/api/devices");
  if (!response.ok) {
    throw new Error("Cihaz listesi alinamadi.");
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
    throw new Error(detail?.message ?? "Baglanti oturumu olusturulamadi.");
  }

  return response.json();
}

export async function listDownloads(): Promise<DownloadPackage[]> {
  const response = await fetch("/api/downloads");
  if (!response.ok) {
    throw new Error("Indirme katalogu alinamadi.");
  }

  return response.json();
}
