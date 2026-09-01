import React from "react";
import { Download, Laptop, ShieldCheck, Sparkles, Trash2 } from "lucide-react";
import { DownloadPackage } from "../api";

interface DownloadsViewProps {
  downloads: DownloadPackage[];
}

export const DownloadsView: React.FC<DownloadsViewProps> = ({ downloads }) => {
  return (
    <div className="content-pane">
      <div className="content-card">
        <h2 className="content-card-title">Kurulum ve Temizleme Paketleri</h2>
        <p className="content-card-copy">
          Hedef istemci bilgisayarlar, teknisyen konsolu ve tek tıkla derin kaldırma araçları.
        </p>

        {/* Zero-Touch Kurulum Bilgi Kartı */}
        <div
          className="stale-data-notice"
          style={{
            background: "rgba(37, 99, 235, 0.08)",
            borderColor: "rgba(37, 99, 235, 0.25)",
            marginTop: "var(--space-3)",
            marginBottom: "var(--space-4)",
          }}
        >
          <Sparkles size={18} style={{ color: "var(--primary)", flexShrink: 0 }} />
          <div style={{ fontSize: "12.5px", color: "var(--text-main)", lineHeight: 1.5 }}>
            <strong>💡 Sıfır Kodlu Kurulum (Zero-Touch):</strong> Hedef bilgisayarda komut çalıştırmanıza gerek yoktur. Sadece <strong>NexMote Agent Setup</strong> paketini kurun. Kurulum biter bitmez cihaz web panelinizde belirecektir; ardından cihazı istediğiniz Şirket ve Departmana atadığınızda tüm güvenlik kuralları cihaza anında uygulanacaktır.
          </div>
        </div>

        <div className="package-list">
          {downloads.map((pkg) => {
            const isCleanup = pkg.fileName.toLowerCase().includes("cleanup");
            const isTechnician = pkg.fileName.toLowerCase().includes("technician");
            const sizeLabel =
              pkg.sizeBytes > 1024 * 1024
                ? `${(pkg.sizeBytes / (1024 * 1024)).toFixed(1)} MB`
                : `${Math.max(1, Math.round(pkg.sizeBytes / 1024))} KB`;

            return (
              <div key={pkg.fileName} className={`package-card ${isCleanup ? "cleanup-card" : ""}`}>
                <div className="package-main">
                  <div className={`package-icon ${isCleanup ? "danger-icon" : isTechnician ? "technician-icon" : ""}`}>
                    {isCleanup ? <Trash2 size={18} /> : isTechnician ? <Laptop size={18} /> : <ShieldCheck size={18} />}
                  </div>
                  <div>
                    <div className="package-name">
                      {pkg.name}
                      {pkg.version && <span className="version-pill"> v{pkg.version}</span>}
                    </div>
                    <div className="mono-text mono-xs" style={{ marginTop: 2 }}>
                      {pkg.fileName} · {sizeLabel} · {pkg.description}
                    </div>
                  </div>
                </div>

                <a
                  href={pkg.url}
                  download
                  className={isCleanup ? "btn-secondary btn-danger-subtle" : "btn-primary"}
                  style={{ height: 32, padding: "0 14px", textDecoration: "none" }}
                >
                  <Download size={14} /> İndir
                </a>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );
};
