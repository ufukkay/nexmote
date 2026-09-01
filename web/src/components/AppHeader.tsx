import React from "react";
import { Bell, LogOut, RefreshCw, Search } from "lucide-react";
import { CurrentUser, DeviceSummary } from "../api";

export interface ActivityItem {
  id: string;
  text: string;
  time: string;
  level: "info" | "success" | "warn";
}

interface AppHeaderProps {
  devices: DeviceSummary[];
  onlineCount: number;
  warningCount: number;
  query: string;
  setQuery: (q: string) => void;
  loading: boolean;
  refresh: (manual?: boolean) => void;
  showNotifications: boolean;
  setShowNotifications: (v: boolean) => void;
  activityLogs: ActivityItem[];
  setActivityLogs: React.Dispatch<React.SetStateAction<ActivityItem[]>>;
  userInitial: string;
  userDisplayName: string;
  roleLabel: string;
  currentUser: CurrentUser | null;
  handleLogout: () => void;
}

export const AppHeader: React.FC<AppHeaderProps> = ({
  devices,
  onlineCount,
  warningCount,
  query,
  setQuery,
  loading,
  refresh,
  showNotifications,
  setShowNotifications,
  activityLogs,
  setActivityLogs,
  userInitial,
  userDisplayName,
  roleLabel,
  currentUser,
  handleLogout,
}) => {
  return (
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

        <div className="user-profile-badge" title={`${currentUser?.email || userDisplayName} (${roleLabel})`}>
          <div className="user-avatar-mini">{userInitial}</div>
          <span className="user-name">{userDisplayName}</span>
          <span className="user-role-badge">{roleLabel}</span>
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
  );
};
