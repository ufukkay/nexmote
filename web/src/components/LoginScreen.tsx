import React from "react";
import { Eye, EyeOff, Server, Shield, ShieldCheck } from "lucide-react";
import { ServerSettings } from "../api";

interface LoginScreenProps {
  settings: ServerSettings;
  authError: string | null;
  isLoggingIn: boolean;
  loginEmail: string;
  setLoginEmail: (v: string) => void;
  loginPassword: string;
  setLoginPassword: (v: string) => void;
  showLoginPassword: boolean;
  setShowLoginPassword: (v: boolean) => void;
  rememberMe: boolean;
  setRememberMe: (v: boolean) => void;
  handleLogin: (e: React.FormEvent) => void;
  mfaChallengeToken: string | null;
  mfaCode: string;
  setMfaCode: (v: string) => void;
  mfaError: string | null;
  mfaVerifying: boolean;
  handleVerifyMfa: (e: React.FormEvent) => void;
  handleCancelMfaChallenge: () => void;
}

export const LoginScreen: React.FC<LoginScreenProps> = ({
  settings,
  authError,
  isLoggingIn,
  loginEmail,
  setLoginEmail,
  loginPassword,
  setLoginPassword,
  showLoginPassword,
  setShowLoginPassword,
  rememberMe,
  setRememberMe,
  handleLogin,
  mfaChallengeToken,
  mfaCode,
  setMfaCode,
  mfaError,
  mfaVerifying,
  handleVerifyMfa,
  handleCancelMfaChallenge,
}) => {
  return (
    <div className="login-container">
      {/* Left Trust Panel */}
      <div className="login-trust-panel">
        <div className="login-trust-logo">
          <div className="login-brand-mark">
            <ShieldCheck size={20} color="#fff" />
          </div>
          <span>NexMote</span>
        </div>

        <div className="login-trust-info">
          <h2 className="login-trust-heading">Kendi sunucunuzda çalışan kurumsal uzaktan yönetim.</h2>
          <div className="login-trust-item">
            <div className="status-dot online" />
            <span className="login-trust-domain">{settings.serverUrl.replace(/^https?:\/\//, "")}</span>
          </div>
          <div className="login-trust-item">
            <Shield size={14} color="#94a3b8" />
            <span>TLS 1.3 · Güvenli oturum trafiği</span>
          </div>
          <div className="login-trust-item">
            <Server size={14} color="#94a3b8" />
            <span>Canlı sinyalleşme hazır</span>
          </div>
        </div>

        <div className="login-footnote">
          © 2026 NexMote · Tüm oturumlar denetim günlüğüne kaydedilir.
        </div>
      </div>

      {/* Right Form Panel */}
      <div className="login-form-panel">
        {mfaChallengeToken ? (
          <div className="login-box">
            <div>
              <h1 className="login-title">Doğrulama Kodu</h1>
              <p className="login-subtitle">Authenticator uygulamanızdaki 6 haneli kodu (veya bir kurtarma kodunu) girin.</p>
            </div>

            <form onSubmit={handleVerifyMfa} className="login-form">
              {mfaError && <div className="login-error-text">{mfaError}</div>}

              <div className="form-group">
                <label className="form-label">Kod</label>
                <div className="form-input-wrapper">
                  <input
                    type="text"
                    inputMode="numeric"
                    autoFocus
                    className="form-input"
                    placeholder="123456"
                    value={mfaCode}
                    onChange={(e) => setMfaCode(e.target.value)}
                    required
                  />
                </div>
              </div>

              <button type="submit" className="btn-primary" data-size="lg" disabled={mfaVerifying}>
                {mfaVerifying ? "Doğrulanıyor..." : "Doğrula ve Giriş Yap"}
              </button>
              <button type="button" className="btn-secondary" onClick={handleCancelMfaChallenge}>
                Geri Dön
              </button>
            </form>
          </div>
        ) : (
          <div className="login-box">
            <div>
              <h1 className="login-title">Oturum Açın</h1>
              <p className="login-subtitle">Kullanıcı kimlik bilgilerinizle konsola bağlanın.</p>
            </div>

            <form onSubmit={handleLogin} className="login-form">
              {authError && (
                <div className="login-error-text">
                  {authError}
                </div>
              )}

              <div className="form-group">
                <label className="form-label">E-posta adresi</label>
                <div className="form-input-wrapper">
                  <input
                    type="email"
                    className="form-input"
                    placeholder="ornek@nexmote.com"
                    value={loginEmail}
                    onChange={(e) => setLoginEmail(e.target.value)}
                    required
                  />
                </div>
              </div>

              <div className="form-group">
                <label className="form-label">Parola</label>
                <div className="form-input-wrapper">
                  <input
                    type={showLoginPassword ? "text" : "password"}
                    className="form-input"
                    placeholder="Parolanız"
                    value={loginPassword}
                    onChange={(e) => setLoginPassword(e.target.value)}
                    required
                  />
                  <button
                    type="button"
                    className="password-toggle-btn"
                    onClick={() => setShowLoginPassword(!showLoginPassword)}
                    title={showLoginPassword ? "Gizle" : "Göster"}
                    aria-label={showLoginPassword ? "Parolayı gizle" : "Parolayı göster"}
                  >
                    {showLoginPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </div>

              <div className="login-options-row">
                <label className="remember-label">
                  <input
                    type="checkbox"
                    checked={rememberMe}
                    onChange={(e) => setRememberMe(e.target.checked)}
                  />
                  Bu cihazda oturumu açık tut
                </label>
              </div>

              <button
                type="submit"
                className="btn-primary"
                data-size="lg"
                disabled={isLoggingIn}
              >
                {isLoggingIn ? "Doğrulanıyor..." : "Giriş Yap"}
              </button>
            </form>
          </div>
        )}
      </div>
    </div>
  );
};
