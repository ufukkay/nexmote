import React from "react";
import { Shield, ShieldCheck } from "lucide-react";

interface InviteAcceptScreenProps {
  invitePreview: { email: string; displayName: string; role: string } | null;
  invitePreviewError: string | null;
  inviteError: string | null;
  invitePassword: string;
  setInvitePassword: (v: string) => void;
  inviteConfirmPassword: string;
  setInviteConfirmPassword: (v: string) => void;
  inviteSubmitting: boolean;
  handleAcceptInvite: (e: React.FormEvent) => void;
}

export const InviteAcceptScreen: React.FC<InviteAcceptScreenProps> = ({
  invitePreview,
  invitePreviewError,
  inviteError,
  invitePassword,
  setInvitePassword,
  inviteConfirmPassword,
  setInviteConfirmPassword,
  inviteSubmitting,
  handleAcceptInvite,
}) => {
  return (
    <div className="login-container">
      <div className="login-trust-panel">
        <div className="login-trust-logo">
          <div className="login-brand-mark">
            <ShieldCheck size={20} color="#fff" />
          </div>
          <span>NexMote</span>
        </div>
        <div className="login-trust-info">
          <h2 className="login-trust-heading">Hesabınızı etkinleştirin.</h2>
          {invitePreview && (
            <div className="login-trust-item">
              <Shield size={14} color="#94a3b8" />
              <span>
                {invitePreview.email} · {invitePreview.role === "Admin" ? "Yönetici" : "Teknisyen"}
              </span>
            </div>
          )}
        </div>
        <div className="login-footnote">© 2026 NexMote · Tüm oturumlar denetim günlüğüne kaydedilir.</div>
      </div>

      <div className="login-form-panel">
        <div className="login-box">
          {invitePreviewError ? (
            <>
              <h1 className="login-title">Davet geçersiz</h1>
              <p className="login-subtitle">{invitePreviewError}</p>
            </>
          ) : !invitePreview ? (
            <p className="login-subtitle">Davet doğrulanıyor...</p>
          ) : (
            <>
              <div>
                <h1 className="login-title">Hoş geldiniz</h1>
                <p className="login-subtitle">{invitePreview.displayName}, devam etmek için bir şifre belirleyin.</p>
              </div>

              <form onSubmit={handleAcceptInvite} className="login-form">
                {inviteError && <div className="login-error-text">{inviteError}</div>}

                <div className="form-group">
                  <label className="form-label">Yeni şifre</label>
                  <div className="form-input-wrapper">
                    <input
                      type="password"
                      className="form-input"
                      value={invitePassword}
                      onChange={(e) => setInvitePassword(e.target.value)}
                      minLength={8}
                      required
                    />
                  </div>
                </div>

                <div className="form-group">
                  <label className="form-label">Şifreyi doğrulayın</label>
                  <div className="form-input-wrapper">
                    <input
                      type="password"
                      className="form-input"
                      value={inviteConfirmPassword}
                      onChange={(e) => setInviteConfirmPassword(e.target.value)}
                      minLength={8}
                      required
                    />
                  </div>
                </div>

                <button type="submit" className="btn-primary" data-size="lg" disabled={inviteSubmitting}>
                  {inviteSubmitting ? "İşleniyor..." : "Hesabı Etkinleştir ve Giriş Yap"}
                </button>
              </form>
            </>
          )}
        </div>
      </div>
    </div>
  );
};
