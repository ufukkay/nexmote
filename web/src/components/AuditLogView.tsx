import React from "react";
import { ActivityLogEntry as AuditLogEntry } from "../api";

interface AuditLogViewProps {
  auditEntries: AuditLogEntry[];
  auditTotal: number;
  auditPage: number;
  auditPageSize: number;
  auditLoading: boolean;
  refreshAuditLog: (page: number) => void;
}

export const AuditLogView: React.FC<AuditLogViewProps> = ({
  auditEntries,
  auditTotal,
  auditPage,
  auditPageSize,
  auditLoading,
  refreshAuditLog,
}) => {
  return (
    <div className="content-pane">
      <div className="content-card">
        <h2 className="content-card-title">Denetim logu ({auditTotal})</h2>
        <p className="content-card-copy">Giriş/çıkış ve yönetimsel eylemlerin denetim kaydı.</p>

        <div className="op-table-container">
          <table className="op-table">
            <thead>
              <tr>
                <th>Zaman</th>
                <th>Kullanıcı</th>
                <th>Eylem</th>
                <th>Hedef</th>
                <th>IP</th>
                <th>Sonuç</th>
              </tr>
            </thead>
            <tbody>
              {auditEntries.map((entry) => (
                <tr key={entry.id}>
                  <td>{new Date(entry.createdAt).toLocaleString("tr-TR")}</td>
                  <td>{entry.userEmail ?? "—"}</td>
                  <td>{entry.action}</td>
                  <td>{entry.targetType ? `${entry.targetType}:${entry.targetId}` : "—"}</td>
                  <td>{entry.ipAddress ?? "—"}</td>
                  <td>{entry.success ? "Başarılı" : "Başarısız"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        <div className="pagination-row">
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
