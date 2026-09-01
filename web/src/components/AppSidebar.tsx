import React from "react";
import {
  Building2,
  ChevronLeft,
  ChevronRight,
  Download,
  LogOut,
  Monitor,
  ScrollText,
  Settings,
  ShieldCheck,
  Users as UsersIcon,
} from "lucide-react";
import { CurrentUser, DeviceGroup, DeviceSummary, UserSummary } from "../api";
import { View } from "../types";

interface AppSidebarProps {
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
  view: View;
  setView: (view: View) => void;
  currentUser: CurrentUser | null;
  onlineCount: number;
  devices: DeviceSummary[];
  rootCompanies: DeviceGroup[];
  users: UserSummary[];
  userInitial: string;
  userDisplayName: string;
  roleLabel: string;
  handleLogout: () => void;
}

export const AppSidebar: React.FC<AppSidebarProps> = ({
  sidebarCollapsed,
  toggleSidebar,
  view,
  setView,
  currentUser,
  onlineCount,
  devices,
  rootCompanies,
  users,
  userInitial,
  userDisplayName,
  roleLabel,
  handleLogout,
}) => {
  return (
    <aside className={`app-sidebar ${sidebarCollapsed ? "collapsed" : ""}`}>
      <div className="sidebar-header">
        <div className="sidebar-brand">
          <div className="sidebar-logo" title="NexMote">
            <ShieldCheck size={18} />
          </div>
          <div className="sidebar-brand-text">
            <span className="sidebar-title">NexMote</span>
            <span className="sidebar-version">v0.7.0 Pro</span>
          </div>
        </div>
        <button
          type="button"
          className="sidebar-toggle-btn"
          onClick={toggleSidebar}
          title={sidebarCollapsed ? "Menüyü Genişlet" : "Menüyü Daralt"}
        >
          {sidebarCollapsed ? <ChevronRight size={15} /> : <ChevronLeft size={15} />}
        </button>
      </div>

      <nav className="sidebar-nav">
        {/* 1. İZLEME & KONTROL */}
        <div className="sidebar-group-title">İzleme &amp; Kontrol</div>
        <ul className="sidebar-nav-list">
          <li>
            <button
              type="button"
              className={`sidebar-item ${view === "devices" || view === "device-detail" ? "active" : ""}`}
              onClick={() => setView("devices")}
              title="Cihazlar"
            >
              <span className="sidebar-item-icon">
                <Monitor size={16} />
              </span>
              <span className="sidebar-item-label">Cihazlar</span>
              <span className={`sidebar-item-badge ${onlineCount > 0 ? "online" : ""}`}>
                {onlineCount}/{devices.length}
              </span>
            </button>
          </li>
          <li>
            <button
              type="button"
              className={`sidebar-item ${view === "downloads" ? "active" : ""}`}
              onClick={() => setView("downloads")}
              title="İndirme Merkezi"
            >
              <span className="sidebar-item-icon">
                <Download size={16} />
              </span>
              <span className="sidebar-item-label">İndirme Merkezi</span>
            </button>
          </li>
        </ul>

        {/* 2. ORGANİZASYON & GÜVENLİK */}
        {currentUser?.role === "Admin" && (
          <>
            <div className="sidebar-group-title">Organizasyon &amp; Güvenlik</div>
            <ul className="sidebar-nav-list">
              <li>
                <button
                  type="button"
                  className={`sidebar-item ${view === "device-groups" ? "active" : ""}`}
                  onClick={() => setView("device-groups")}
                  title="Şirketler, Departmanlar ve Güvenlik Politikaları"
                >
                  <span className="sidebar-item-icon">
                    <Building2 size={16} />
                  </span>
                  <span className="sidebar-item-label">Şirketler &amp; Güvenlik</span>
                  <span className="sidebar-item-badge">{rootCompanies.length}</span>
                </button>
              </li>
            </ul>

            {/* 3. YÖNETİM & SİSTEM */}
            <div className="sidebar-group-title">Yönetim &amp; Sistem</div>
            <ul className="sidebar-nav-list">
              <li>
                <button
                  type="button"
                  className={`sidebar-item ${view === "users" ? "active" : ""}`}
                  onClick={() => setView("users")}
                  title="Kullanıcı Yönetimi"
                >
                  <span className="sidebar-item-icon">
                    <UsersIcon size={16} />
                  </span>
                  <span className="sidebar-item-label">Kullanıcılar</span>
                  <span className="sidebar-item-badge">{users.length}</span>
                </button>
              </li>
              <li>
                <button
                  type="button"
                  className={`sidebar-item ${view === "audit-log" ? "active" : ""}`}
                  onClick={() => setView("audit-log")}
                  title="Denetim Logu"
                >
                  <span className="sidebar-item-icon">
                    <ScrollText size={16} />
                  </span>
                  <span className="sidebar-item-label">Denetim Logu</span>
                </button>
              </li>
            </ul>
          </>
        )}

        <div className="sidebar-group-title">Sistem</div>
        <ul className="sidebar-nav-list">
          <li>
            <button
              type="button"
              className={`sidebar-item ${view === "settings" ? "active" : ""}`}
              onClick={() => setView("settings")}
              title="Sistem &amp; Hesap Ayarları"
            >
              <span className="sidebar-item-icon">
                <Settings size={16} />
              </span>
              <span className="sidebar-item-label">Sistem &amp; Hesap</span>
            </button>
          </li>
        </ul>
      </nav>

      <div className="sidebar-footer">
        <div className="sidebar-user-card">
          <div className="user-avatar-mini">{userInitial}</div>
          <div className="sidebar-user-info">
            <div className="sidebar-user-name" title={currentUser?.email || userDisplayName}>
              {userDisplayName}
            </div>
            <div className="sidebar-user-role">{roleLabel}</div>
          </div>
          <button
            type="button"
            className="user-logout-mini-btn"
            onClick={handleLogout}
            title="Oturumu Kapat"
          >
            <LogOut size={13} />
          </button>
        </div>
      </div>
    </aside>
  );
};
