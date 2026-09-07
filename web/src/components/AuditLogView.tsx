import React, { useState } from "react";
import { ActivityLogEntry as AuditLogEntry } from "../api";
import {
  ShieldAlert,
  Terminal,
  Monitor,
  Trash2,
  Settings,
  LogIn,
  LogOut,
  UserPlus,
  Mail,
  Info,
  CheckCircle2,
  XCircle,
  Copy,
  ChevronDown,
  ChevronRight
} from "lucide-react";

interface AuditLogViewProps {
  auditEntries: AuditLogEntry[];
  auditTotal: number;
  auditPage: number;
  auditPageSize: number;
  auditLoading: boolean;
  refreshAuditLog: (page: number) => void;
}

const ACTION_METADATA: Record<string, { label: string; color: string; icon: React.ReactNode }> = {
  "session.start": {
    label: "Uzak Bağlantı",
    color: "rgba(59, 130, 246, 0.15)",
    icon: <Monitor size={14} className="text-blue-500" />
  },
  "command.execute": {
    label: "Terminal Komutu",
    color: "rgba(168, 85, 247, 0.15)",
    icon: <Terminal size={14} className="text-purple-500" />
  },
  "device.uninstall_app": {
    label: "Uygulama Kaldırma",
    color: "rgba(245, 158, 11, 0.15)",
    icon: <Trash2 size={14} className="text-amber-500" />
  },
  "device.uninstall_agent": {
    label: "Ajan Kaldırma",
    color: "rgba(239, 68, 68, 0.15)",
    icon: <Trash2 size={14} className="text-red-500" />
  },
  "device.delete": {
    label: "Cihaz Silme",
    color: "rgba(239, 68, 68, 0.15)",
    icon: <Trash2 size={14} className="text-red-500" />
  },
  "settings.update": {
    label: "Ayar Değişikliği",
    color: "rgba(234, 179, 8, 0.15)",
    icon: <Settings size={14} className="text-yellow-500" />
  },
  "settings.smtp_test": {
    label: "SMTP Testi",
    color: "rgba(14, 165, 233, 0.15)",
    icon: <Mail size={14} className="text-sky-500" />
  },
  "agent.update_requested": {
    label: "Ajan Güncelleme",
    color: "rgba(16, 185, 129, 0.15)",
    icon: <Info size={14} className="text-emerald-500" />
  },
  "login.success": {
    label: "Giriş Başarılı",
    color: "rgba(16, 185, 129, 0.15)",
    icon: <LogIn size={14} className="text-emerald-500" />
  },
  "login.failed": {
    label: "Hatalı Giriş",
    color: "rgba(239, 68, 68, 0.15)",
    icon: <ShieldAlert size={14} className="text-red-500" />
  },
  "logout": {
    label: "Çıkış Yapıldı",
    color: "rgba(100, 116, 139, 0.15)",
    icon: <LogOut size={14} className="text-slate-400" />
  },
  "user.create": {
    label: "Kullanıcı Eklendi",
    color: "rgba(99, 102, 241, 0.15)",
    icon: <UserPlus size={14} className="text-indigo-500" />
  },
  "user.invite": {
    label: "Davet Gönderildi",
    color: "rgba(99, 102, 241, 0.15)",
    icon: <Mail size={14} className="text-indigo-500" />
  }
};

export const AuditLogView: React.FC<AuditLogViewProps> = ({
  auditEntries,
  auditTotal,
  auditPage,
  auditPageSize,
  auditLoading,
  refreshAuditLog,
}) => {
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [copiedId, setCopiedId] = useState<string | null>(null);

  const copyToClipboard = (text: string, id: string) => {
    navigator.clipboard.writeText(text);
    setCopiedId(id);
    setTimeout(() => setCopiedId(null), 2000);
  };

  return (
    <div className="content-pane">
      <div className="content-card">
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: "var(--space-3)" }}>
          <div>
            <h2 className="content-card-title">Denetim &amp; Aktivite Günlüğü ({auditTotal})</h2>
            <p className="content-card-copy">
              Uzak oturumlar, terminal komutları, kimlik doğrulama ve yönetimsel eylemlerin uçtan uca aktör ve korelasyon kayıtları.
            </p>
          </div>
          <button
            type="button"
            className="btn-secondary"
            disabled={auditLoading}
            onClick={() => refreshAuditLog(auditPage)}
            style={{ fontSize: "12px", padding: "6px 12px" }}
          >
            {auditLoading ? "Yenileniyor..." : "Yenile"}
          </button>
        </div>

        <div className="op-table-container">
          <table className="op-table">
            <thead>
              <tr>
                <th style={{ width: "32px" }}></th>
                <th>Zaman</th>
                <th>Aktör (Kullanıcı)</th>
                <th>Eylem</th>
                <th>Hedef</th>
                <th>IP Adresi</th>
                <th>Sonuç</th>
                <th>Korelasyon ID</th>
              </tr>
            </thead>
            <tbody>
              {auditEntries.length === 0 ? (
                <tr>
                  <td colSpan={8} style={{ textAlign: "center", padding: "32px", color: "var(--text-muted)" }}>
                    Henüz kayıtlı bir aktivite bulunmuyor.
                  </td>
                </tr>
              ) : (
                auditEntries.map((entry) => {
                  const meta = ACTION_METADATA[entry.action] || {
                    label: entry.action,
                    color: "rgba(148, 163, 184, 0.15)",
                    icon: <Info size={14} />
                  };
                  const isExpanded = expandedId === entry.id;

                  return (
                    <React.Fragment key={entry.id}>
                      <tr
                        style={{ cursor: entry.detailsJson ? "pointer" : "default" }}
                        onClick={() => {
                          if (entry.detailsJson) {
                            setExpandedId(isExpanded ? null : entry.id);
                          }
                        }}
                      >
                        <td>
                          {entry.detailsJson ? (
                            isExpanded ? <ChevronDown size={14} /> : <ChevronRight size={14} />
                          ) : null}
                        </td>
                        <td style={{ whiteSpace: "nowrap", fontSize: "12px" }}>
                          {new Date(entry.createdAt).toLocaleString("tr-TR")}
                        </td>
                        <td>
                          <span style={{ fontWeight: 500 }}>{entry.userEmail ?? "Sistem / Anonim"}</span>
                        </td>
                        <td>
                          <span
                            style={{
                              display: "inline-flex",
                              alignItems: "center",
                              gap: "6px",
                              padding: "2px 8px",
                              borderRadius: "6px",
                              backgroundColor: meta.color,
                              fontSize: "12px",
                              fontWeight: 500
                            }}
                          >
                            {meta.icon}
                            {meta.label}
                          </span>
                        </td>
                        <td style={{ fontSize: "12px", color: "var(--text-secondary)" }}>
                          {entry.targetType ? `${entry.targetType}: ${entry.targetId ?? ""}` : "—"}
                        </td>
                        <td style={{ fontFamily: "monospace", fontSize: "11px" }}>
                          {entry.ipAddress ?? "—"}
                        </td>
                        <td>
                          {entry.success ? (
                            <span style={{ display: "inline-flex", alignItems: "center", gap: "4px", color: "#10b981", fontSize: "12px" }}>
                              <CheckCircle2 size={13} /> Başarılı
                            </span>
                          ) : (
                            <span style={{ display: "inline-flex", alignItems: "center", gap: "4px", color: "#ef4444", fontSize: "12px" }}>
                              <XCircle size={13} /> Başarısız
                            </span>
                          )}
                        </td>
                        <td>
                          {entry.correlationId ? (
                            <button
                              type="button"
                              className="btn-ghost"
                              style={{
                                fontFamily: "monospace",
                                fontSize: "11px",
                                padding: "2px 6px",
                                height: "auto",
                                display: "inline-flex",
                                alignItems: "center",
                                gap: "4px"
                              }}
                              onClick={(e) => {
                                e.stopPropagation();
                                copyToClipboard(entry.correlationId!, entry.id);
                              }}
                              title="Korelasyon ID kopyala"
                            >
                              <Copy size={11} />
                              {copiedId === entry.id ? "Kopyalandı" : entry.correlationId.slice(0, 8) + "..."}
                            </button>
                          ) : (
                            <span style={{ color: "var(--text-muted)", fontSize: "11px" }}>—</span>
                          )}
                        </td>
                      </tr>
                      {isExpanded && entry.detailsJson && (
                        <tr>
                          <td colSpan={8} style={{ backgroundColor: "var(--bg-subtle)", padding: "12px 16px" }}>
                            <div style={{ fontSize: "12px", fontWeight: 600, marginBottom: "4px", color: "var(--text-secondary)" }}>
                              İşlem Detayları &amp; Bağlam:
                            </div>
                            <pre
                              style={{
                                margin: 0,
                                padding: "8px 12px",
                                borderRadius: "6px",
                                backgroundColor: "var(--bg-card)",
                                border: "1px solid var(--border-color)",
                                fontSize: "11px",
                                fontFamily: "monospace",
                                whiteSpace: "pre-wrap",
                                wordBreak: "break-word"
                              }}
                            >
                              {(() => {
                                try {
                                  return JSON.stringify(JSON.parse(entry.detailsJson), null, 2);
                                } catch {
                                  return entry.detailsJson;
                                }
                              })()}
                            </pre>
                          </td>
                        </tr>
                      )}
                    </React.Fragment>
                  );
                })
              )}
            </tbody>
          </table>
        </div>

        <div className="pagination-row" style={{ marginTop: "var(--space-3)" }}>
          <button
            className="btn-secondary"
            disabled={auditLoading || auditPage <= 1}
            onClick={() => refreshAuditLog(auditPage - 1)}
          >
            Önceki
          </button>
          <span>
            Sayfa {auditPage} / {Math.max(1, Math.ceil(auditTotal / auditPageSize))}
          </span>
          <button
            className="btn-secondary"
            disabled={auditLoading || auditPage * auditPageSize >= auditTotal}
            onClick={() => refreshAuditLog(auditPage + 1)}
          >
            Sonraki
          </button>
        </div>
      </div>
    </div>
  );
};
