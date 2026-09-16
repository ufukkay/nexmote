import React, { useState } from "react";
import { AlertCircle, AlertTriangle, Ban, Check, Copy, Eye, EyeOff, KeyRound, RotateCcw, ShieldOff, Sparkles, Trash2, Users as UsersIcon, X } from "lucide-react";
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
  handleResetUserMfa: (u: UserSummary) => Promise<void>;
  handleAdminChangePassword: (u: UserSummary, newPassword: string) => Promise<void>;
  handleDeleteUser: (u: UserSummary) => Promise<void>;
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
  handleAdminChangePassword,
  handleDeleteUser,
}) => {
  // Modal State: Şifre Değiştirme
  const [passwordModalUser, setPasswordModalUser] = useState<UserSummary | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [passwordCopied, setPasswordCopied] = useState(false);
  const [passwordError, setPasswordError] = useState("");

  // Modal State: MFA Kaldırma Onayı
  const [mfaModalUser, setMfaModalUser] = useState<UserSummary | null>(null);
  const [mfaBusy, setMfaBusy] = useState(false);

  // Modal State: Kullanıcı Silme Onayı
  const [deleteModalUser, setDeleteModalUser] = useState<UserSummary | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);

  function generateStrongPassword() {
    const chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789!@#$%&*";
    let pwd = "";
    const randValues = new Uint32Array(14);
    window.crypto.getRandomValues(randValues);
    for (let i = 0; i < 14; i++) {
      pwd += chars[randValues[i] % chars.length];
    }
    setNewPassword(pwd);
    setShowPassword(true);
    setPasswordError("");
  }

  function copyPasswordToClipboard() {
    if (!newPassword) return;
    navigator.clipboard.writeText(newPassword);
    setPasswordCopied(true);
    setTimeout(() => setPasswordCopied(false), 2000);
  }

  async function onConfirmPasswordChange(e: React.FormEvent) {
    e.preventDefault();
    if (!passwordModalUser) return;
    if (newPassword.length < 8) {
      setPasswordError("Şifre en az 8 karakter olmalıdır.");
      return;
    }
    setPasswordBusy(true);
    setPasswordError("");
    try {
      await handleAdminChangePassword(passwordModalUser, newPassword);
      setPasswordModalUser(null);
      setNewPassword("");
    } catch (err) {
      setPasswordError(err instanceof Error ? err.message : "Şifre değiştirilemedi.");
    } finally {
      setPasswordBusy(false);
    }
  }

  async function onConfirmResetMfa() {
    if (!mfaModalUser) return;
    setMfaBusy(true);
    try {
      await handleResetUserMfa(mfaModalUser);
      setMfaModalUser(null);
    } finally {
      setMfaBusy(false);
    }
  }

  async function onConfirmDeleteUser() {
    if (!deleteModalUser) return;
    setDeleteBusy(true);
    try {
      await handleDeleteUser(deleteModalUser);
      setDeleteModalUser(null);
    } finally {
      setDeleteBusy(false);
    }
  }

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
                        title="Şifresini Değiştir"
                        onClick={() => {
                          setPasswordModalUser(u);
                          setNewPassword("");
                          setPasswordError("");
                        }}
                      >
                        <KeyRound size={14} />
                      </button>
                      {u.mfaEnabled && (
                        <button
                          className="icon-action-btn"
                          title="MFA'yı Kaldır"
                          onClick={() => setMfaModalUser(u)}
                        >
                          <ShieldOff size={14} />
                        </button>
                      )}
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
                      <button
                        className="icon-action-btn"
                        title={
                          u.id === currentUser?.id
                            ? "Kendi hesabınızı silemezsiniz"
                            : "Teknisyeni Sil"
                        }
                        disabled={u.id === currentUser?.id}
                        onClick={() => setDeleteModalUser(u)}
                        style={{ color: u.id === currentUser?.id ? undefined : "var(--danger, #ef4444)" }}
                      >
                        <Trash2 size={14} />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Modal: Şifre Değiştirme */}
      {passwordModalUser && (
        <div className="modal-backdrop" onClick={() => !passwordBusy && setPasswordModalUser(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div className="modal-title-with-icon">
                <div className="modal-icon-badge">
                  <KeyRound size={18} />
                </div>
                <div>
                  <h3 className="modal-title">Şifre Değiştir</h3>
                  <p className="modal-subtitle">
                    {passwordModalUser.displayName || passwordModalUser.email} ({passwordModalUser.email})
                  </p>
                </div>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => !passwordBusy && setPasswordModalUser(null)}
              >
                <X size={16} />
              </button>
            </div>

            <form onSubmit={onConfirmPasswordChange}>
              <div className="modal-body">
                <div className="stale-data-notice" style={{ marginBottom: "var(--space-3)" }}>
                  <AlertCircle size={14} />
                  <span>
                    Yeni şifre belirlendiğinde kullanıcının tüm aktif oturumları anında iptal edilir ve yeni şifresiyle tekrar giriş yapması gerekir.
                  </span>
                </div>

                <div className="form-group" style={{ marginBottom: "var(--space-3)" }}>
                  <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: "4px" }}>
                    <label className="form-label" style={{ margin: 0 }}>Yeni Şifre</label>
                    <button
                      type="button"
                      className="btn-secondary"
                      style={{ height: "26px", fontSize: "11px", padding: "0 8px" }}
                      onClick={generateStrongPassword}
                    >
                      <Sparkles size={12} />
                      Rastgele Üret
                    </button>
                  </div>
                  <div style={{ position: "relative", display: "flex", alignItems: "center" }}>
                    <input
                      type={showPassword ? "text" : "password"}
                      className="form-input"
                      style={{ paddingRight: "70px" }}
                      placeholder="En az 8 karakter..."
                      value={newPassword}
                      onChange={(e) => {
                        setNewPassword(e.target.value);
                        setPasswordError("");
                      }}
                      required
                    />
                    <div style={{ position: "absolute", right: "6px", display: "flex", gap: "2px" }}>
                      {newPassword && (
                        <button
                          type="button"
                          className="icon-action-btn"
                          title="Şifreyi Kopyala"
                          onClick={copyPasswordToClipboard}
                        >
                          {passwordCopied ? <Check size={14} /> : <Copy size={14} />}
                        </button>
                      )}
                      <button
                        type="button"
                        className="icon-action-btn"
                        title={showPassword ? "Gizle" : "Göster"}
                        onClick={() => setShowPassword(!showPassword)}
                      >
                        {showPassword ? <EyeOff size={14} /> : <Eye size={14} />}
                      </button>
                    </div>
                  </div>
                </div>

                {passwordError && (
                  <div className="error-banner" style={{ marginTop: "var(--space-2)", fontSize: "12px" }}>
                    {passwordError}
                  </div>
                )}
              </div>

              <div className="modal-footer">
                <button
                  type="button"
                  className="btn-secondary"
                  onClick={() => setPasswordModalUser(null)}
                  disabled={passwordBusy}
                >
                  Vazgeç
                </button>
                <button
                  type="submit"
                  className="btn-primary"
                  disabled={passwordBusy || !newPassword}
                >
                  <KeyRound size={14} />
                  {passwordBusy ? "Kaydediliyor..." : "Şifreyi Güncelle"}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* Modal: MFA Kaldırma Onayı */}
      {mfaModalUser && (
        <div className="modal-backdrop" onClick={() => !mfaBusy && setMfaModalUser(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div className="modal-title-with-icon">
                <div className="modal-icon-badge">
                  <ShieldOff size={18} />
                </div>
                <div>
                  <h3 className="modal-title">MFA Kaldırma Onayı</h3>
                  <p className="modal-subtitle">{mfaModalUser.email}</p>
                </div>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => !mfaBusy && setMfaModalUser(null)}
              >
                <X size={16} />
              </button>
            </div>

            <div className="modal-body">
              <p style={{ margin: 0, fontSize: "13.5px", lineHeight: "1.5", color: "var(--text-main)" }}>
                <strong>{mfaModalUser.displayName || mfaModalUser.email}</strong> kullanıcısının iki adımlı doğrulaması (MFA/TOTP) kaldırılacaktır.
              </p>
              <p style={{ marginTop: "var(--space-2)", fontSize: "12.5px", color: "var(--text-dim)", lineHeight: "1.4" }}>
                Kullanıcı bir sonraki oturum açışında ek doğrulama kodu girmeden doğrudan şifresiyle giriş yapabilecek ve varsa kilitlenmiş oturumu açılacaktır.
              </p>
            </div>

            <div className="modal-footer">
              <button
                type="button"
                className="btn-secondary"
                onClick={() => setMfaModalUser(null)}
                disabled={mfaBusy}
              >
                Vazgeç
              </button>
              <button
                type="button"
                className="btn-primary"
                onClick={onConfirmResetMfa}
                disabled={mfaBusy}
              >
                <ShieldOff size={14} />
                {mfaBusy ? "Kaldırılıyor..." : "MFA'yı Kaldır"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Modal: Kullanıcı Silme Onayı */}
      {deleteModalUser && (
        <div className="modal-backdrop" onClick={() => !deleteBusy && setDeleteModalUser(null)}>
          <div className="modal-dialog" onClick={(e) => e.stopPropagation()}>
            <div className="modal-header">
              <div className="modal-title-with-icon">
                <div className="modal-icon-badge danger" style={{ color: "var(--danger, #ef4444)" }}>
                  <AlertTriangle size={18} />
                </div>
                <div>
                  <h3 className="modal-title">Teknisyeni Sil</h3>
                  <p className="modal-subtitle">{deleteModalUser.email}</p>
                </div>
              </div>
              <button
                type="button"
                className="modal-close-btn"
                onClick={() => !deleteBusy && setDeleteModalUser(null)}
              >
                <X size={16} />
              </button>
            </div>

            <div className="modal-body">
              <div className="stale-data-notice" style={{ borderColor: "rgba(239, 68, 68, 0.4)", background: "rgba(239, 68, 68, 0.05)" }}>
                <AlertCircle size={14} style={{ color: "var(--danger, #ef4444)" }} />
                <span style={{ color: "var(--danger, #ef4444)" }}>
                  <strong>{deleteModalUser.displayName || deleteModalUser.email}</strong> kullanıcısı ve tüm oturumları sistemden kalıcı olarak silinecektir. Bu işlem geri alınamaz!
                </span>
              </div>
            </div>

            <div className="modal-footer">
              <button
                type="button"
                className="btn-secondary"
                onClick={() => setDeleteModalUser(null)}
                disabled={deleteBusy}
              >
                Vazgeç
              </button>
              <button
                type="button"
                className="btn-danger"
                onClick={onConfirmDeleteUser}
                disabled={deleteBusy}
              >
                <Trash2 size={14} />
                {deleteBusy ? "Siliniyor..." : "Kalıcı Olarak Sil"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
