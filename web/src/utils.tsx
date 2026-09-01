import React from "react";
import { ArrowUpDown, ChevronDown, ChevronUp } from "lucide-react";
import { SortDirection, SortField } from "./types";

export function isVersionOlder(installed?: string | null, latest?: string | null): boolean {
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

export function renderSortIndicator(field: SortField, currentField: SortField, direction: SortDirection) {
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
 */
export function formatLastSeen(lastSeenAt: string): string {
  const diffMs = Date.now() - new Date(lastSeenAt).getTime();
  const diffMinutes = Math.floor(diffMs / 60000);

  if (diffMinutes < 1) return "az önce";
  if (diffMinutes < 60) return `${diffMinutes} dk önce`;

  const diffHours = Math.floor(diffMinutes / 60);
  if (diffHours < 24) return `${diffHours} sa önce`;

  const diffDays = Math.floor(diffHours / 24);
  return `${diffDays} gün önce`;
}

export const ALERT_TYPE_LABELS: Record<string, string> = {
  Offline: "Cihaz uzun süredir çevrimdışı",
  DiskLow: "Disk alanı azaldı",
  CpuHigh: "CPU kullanımı yüksek",
  MemoryHigh: "RAM kullanımı yüksek"
};

export function describeAlertType(alertType: string): string {
  return ALERT_TYPE_LABELS[alertType] ?? alertType;
}

export function cleanUserName(rawUser?: string): string {
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

export function formatUptime(seconds: number): string {
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

export function formatOsName(rawOs?: string): string {
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

export function renderSparkline(data: number[], color: string, maxVal: number) {
  if (data.length < 2) {
    return <div className="sparkline-placeholder">Canlı veri toplanıyor...</div>;
  }

  const width = 280;
  const height = 48;
  const padding = 4;
  const pts = data.map((val, idx) => {
    const x = padding + (idx / (data.length - 1)) * (width - padding * 2);
    const clamped = Math.max(0, Math.min(val, maxVal));
    const y = height - padding - (clamped / maxVal) * (height - padding * 2);
    return `${x.toFixed(1)},${y.toFixed(1)}`;
  });

  const polylinePoints = pts.join(" ");
  const firstPt = pts[0].split(",");
  const lastPt = pts[pts.length - 1].split(",");
  const fillPoints = `${firstPt[0]},${height} ${polylinePoints} ${lastPt[0]},${height}`;

  return (
    <svg width={width} height={height} className="sparkline-svg" viewBox={`0 0 ${width} ${height}`}>
      <defs>
        <linearGradient id={`grad-${color.replace("#", "")}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.25" />
          <stop offset="100%" stopColor={color} stopOpacity="0.0" />
        </linearGradient>
      </defs>
      <polygon points={fillPoints} fill={`url(#grad-${color.replace("#", "")})`} />
      <polyline
        points={polylinePoints}
        fill="none"
        stroke={color}
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
