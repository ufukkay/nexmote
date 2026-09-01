import React from "react";
import { AlertCircle, Ban, RotateCcw, Shield, Users as UsersIcon } from "lucide-react";
import { CurrentUser, UserSummary } from "../api";

interface UsersViewProps {
  currentUser: CurrentUser | null;
  newUserMode: "invite" | "password";
  setNewUserMode: (mode: "invite" | "password") => void;
  createdUserCredentials: { email: string; temporaryPassword: string } | null;
  invitedEmail: string | null;
  newUserEmail: string;
  setNewUserEmail: (v: string) => void;
  newUserDisplayName: string;
  setNewUserDisplayName: (v: string) => void;
  newUserRole: "Admin" | "Technician";
  setNewUserRole: (r: "Admin" | "Technician") => void;
  creatingUser: boolean;
  handleCreateUser: (e: React.FormEvent) => void;
  users: UserSummary[];
  handleSetUserRole: (id: string, role: "Admin" | "Technician") => void;
  handleToggleUserActive: (u: UserSummary) => void;
  handleResetUserMfa: (u: UserSummary) => void;
}

export const UsersView: React.FC<UsersViewProps> = ({
  currentUser,
  newUserMode,
  setNewUserMode,
  createdUserCredentials,
  invitedEmail,
  newUserEmail,
  setNewUserEmail,
  newUserDisplayName,
  setNewUserDisplayName,
  newUserRole,
  setNewUserRole,
  creatingUser,
  handleCreateUser,
  users,
  handleSetUserRole,
  handleToggleUserActive,
  handleResetUserMfa,
}) => {
  return (
    <div className="content-pane">
      <div className="content-card">
        <h2 className="content-card-title">Yeni kullanıcı oluştur</h2>
        <p className="content-card-copy">
          {newUserMode === "invite"
            ? "Yeni bir Admin veya Teknisyen hesabına, kendi şifresini belirleyebilecekleri bir davet e-postası gönderilir."
            : "Yeni bir Admin veya Teknisyen hesabı için tek seferlik geçici şifre üretilir."}
        </p>

        <div className="login-options-row" style={{ marginBottom: "var(--space-3)" }}>
          <label className="remember-label">
            <input
              type="radio"
              checked={newUserMode === "invite"}
              onChange={() => setNewUserMode("invite")}
            />
            E-posta ile davet et
          </label>
          <label className="remember-label">
            <input
              type="radio"
              checked={newUserMode === "password"}
              onChange={() => setNewUserMode("password")}
            />
            Geçici şifre oluştur
          </label>
        </div>

        {createdUserCredentials && (
          <div className="stale-data-notice">
            <AlertCircle size={14} />
            <span>
              <strong>{createdUserCredentials.email}</strong> için geçici şifre:{" "}
              <code>{createdUserCredentials.temporaryPassword}</code> — bu şifreyi güvenli bir kanaldan kullanıcıya iletin, bir daha gösterilmeyecek.
            </span>
          </div>
        )}

        {invitedEmail && (
          <div className="stale-data-notice">
            <AlertCircle size={14} />
            <span>
              <strong>{invitedEmail}</strong> adresine davet e-postası gönderildi.
            </span>
          </div>
        )}

        <form onSubmit={handleCreateUser} className="settings-form">
          <div className="form-group">
            <label className="form-label">E-posta</label>
            <input
              type="email"
              className="form-input"
              value={newUserEmail}
              onChange={(e) => setNewUserEmail(e.target.value)}
              required
            />
          </div>
          <div className="form-group">
            <label className="form-label">Görünen ad</label>
            <input
              type="text"
              className="form-input"
              value={newUserDisplayName}
              onChange={(e) => setNewUserDisplayName(e.target.value)}
            />
          </div>
          <div className="form-group">
            <label className="form-label">Rol</label>
            <select
              className="form-input"
              value={newUserRole}
              onChange={(e) => setNewUserRole(e.target.value as "Admin" | "Technician")}
            >
              <option value="Technician">Teknisyen</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
          <button type="submit" className="btn-primary" data-width="fixed" disabled={creatingUser}>
            <UsersIcon size={14} />
            {creatingUser ? "İşleniyor..." : newUserMode === "invite" ? "Davet Gönder" : "Kullanıcı Oluştur"}
          </button>
        </form>
      </div>

      <div className="content-card">
        <h2 className="content-card-title">Kullanıcılar ({users.length})</h2>
        <div className="op-table-container">
          <table className="op-table">
            <thead>
              <tr>
                <th>E-posta</th>
                <th>Ad</th>
                <th>Rol</th>
                <th>MFA</th>
                <th>Durum</th>
                <th>Son giriş</th>
                <th>Aksiyonlar</th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => (
                <tr key={u.id}>
                  <td>{u.email}</td>
                  <td>{u.displayName}</td>
                  <td>
                    <select
                      className="form-input"
                      value={u.role}
                      disabled={u.id === currentUser?.id}
                      title={u.id === currentUser?.id ? "Kendi rolünüzü değiştiremezsiniz" : undefined}
                      onChange={(e) => handleSetUserRole(u.id, e.target.value as "Admin" | "Technician")}
                    >
                      <option value="Technician">Teknisyen</option>
                      <option value="Admin">Admin</option>
                    </select>
                  </td>
                  <td>{u.mfaEnabled ? "Açık" : "Kapalı"}</td>
                  <td>{u.isActive ? "Aktif" : "Devre dışı"}</td>
                  <td>{u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString("tr-TR") : "—"}</td>
                  <td>
                    <div className="row-action-group">
                      <button
                        className="icon-action-btn"
                        title={
                          u.id === currentUser?.id
                            ? "Kendi hesabınızı devre dışı bırakamazsınız"
                            : u.isActive
                            ? "Devre dışı bırak"
                            : "Etkinleştir"
                        }
                        disabled={u.isActive && u.id === currentUser?.id}
                        onClick={() => handleToggleUserActive(u)}
                      >
                        {u.isActive ? <Ban size={14} /> : <RotateCcw size={14} />}
                      </button>
                      {u.mfaEnabled && (
                        <button
                          className="icon-action-btn"
                          title="MFA'yı sıfırla"
                          onClick={() => handleResetUserMfa(u)}
                        >
                          <Shield size={14} />
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
};
