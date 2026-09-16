using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app, RouteGroupBuilder authed, RouteGroupBuilder admin)
    {
        // Public endpoints
        app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

        app.MapGet("/downloads/{fileName}", (string fileName, DownloadCatalog downloads) =>
        {
            var file = downloads.GetFile(fileName);
            return file is null
                ? Results.NotFound(new { message = "İndirme paketi bulunamadı." })
                : Results.File(file.Path, file.ContentType, file.FileName);
        });

        app.MapGet("/api/downloads", (DownloadCatalog downloads) => Results.Ok(downloads.List()));

        app.MapGet("/api/updates/check", (IConfiguration config, DownloadCatalog downloads, HttpContext httpContext) =>
        {
            var configuredPublicUrl = config["PublicUrl"]?.TrimEnd('/');
            var requestBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}".TrimEnd('/');
            var baseUrl = !string.IsNullOrWhiteSpace(configuredPublicUrl) && !configuredPublicUrl.Contains("nexmote.com", StringComparison.OrdinalIgnoreCase)
                ? configuredPublicUrl
                : requestBaseUrl;
            var versions = downloads.GetVersionInfo();
            var agentIntegrity = downloads.GetIntegrity("NexMote-Agent-Setup.msi");
            var technicianIntegrity = downloads.GetIntegrity("NexMote-Technician-Setup.msi");
            return Results.Ok(new
            {
                agent = new
                {
                    version = versions.Agent.Version,
                    downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Agent-Setup.msi",
                    releaseNotes = versions.Agent.ReleaseNotes,
                    sha256 = versions.Agent.Sha256 ?? agentIntegrity?.Sha256,
                    sizeBytes = versions.Agent.SizeBytes ?? agentIntegrity?.SizeBytes
                },
                technician = new
                {
                    version = versions.Technician.Version,
                    downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Technician-Setup.msi",
                    releaseNotes = versions.Technician.ReleaseNotes,
                    sha256 = versions.Technician.Sha256 ?? technicianIntegrity?.Sha256,
                    sizeBytes = versions.Technician.SizeBytes ?? technicianIntegrity?.SizeBytes
                }
            });
        });

        // Authed endpoints
        authed.MapGet("/alerts/active", (AlertService alerts) => Results.Ok(alerts.ListActive()));
        authed.MapGet("/server-metrics", (ServerTelemetryService metrics) => Results.Ok(metrics.GetMetrics()));

        // Admin Settings endpoints
        admin.MapGet("/settings", (IDbContextFactory<AppDbContext> dbFactory) =>
        {
            using var db = dbFactory.CreateDbContext();
            var setting = db.ServerSettings.AsNoTracking().First();
            return Results.Ok(new ServerSettingsContract(
                setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode,
                SmtpHost: setting.SmtpHost, SmtpPort: setting.SmtpPort, SmtpUsername: setting.SmtpUsername,
                SmtpPassword: null, SmtpFromAddress: setting.SmtpFromAddress, SmtpFromName: setting.SmtpFromName,
                SmtpSslMode: setting.SmtpSslMode ?? "Auto",
                AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
                AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
                AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
                AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
                AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
        });

        admin.MapPost("/settings", (
            ServerSettingsContract request,
            HttpContext http,
            IDbContextFactory<AppDbContext> dbFactory,
            EmailService email,
            AuditLogService auditLog) =>
        {
            using var db = dbFactory.CreateDbContext();
            var setting = db.ServerSettings.First();
            setting.ServerUrl = request.ServerUrl.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(request.EnrollmentKey))
            {
                setting.EnrollmentKey = request.EnrollmentKey.Trim();
            }
            setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
            setting.DefaultLocationCode = request.DefaultLocationCode;
            setting.SmtpHost = request.SmtpHost;
            setting.SmtpPort = request.SmtpPort <= 0 ? 465 : request.SmtpPort;
            setting.SmtpUsername = request.SmtpUsername;
            setting.SmtpFromAddress = request.SmtpFromAddress;
            setting.SmtpFromName = request.SmtpFromName;
            setting.SmtpSslMode = string.IsNullOrWhiteSpace(request.SmtpSslMode) ? "Auto" : request.SmtpSslMode;
            if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
            {
                setting.SmtpPasswordEncrypted = email.EncryptPassword(request.SmtpPassword);
            }
            setting.AlertsEnabled = request.AlertsEnabled;
            setting.AlertRecipientEmails = request.AlertRecipientEmails;
            setting.AlertOfflineEnabled = request.AlertOfflineEnabled;
            setting.AlertOfflineMinutes = Math.Max(1, request.AlertOfflineMinutes);
            setting.AlertDiskLowEnabled = request.AlertDiskLowEnabled;
            setting.AlertDiskLowMb = Math.Max(0, request.AlertDiskLowMb);
            setting.AlertCpuHighEnabled = request.AlertCpuHighEnabled;
            setting.AlertCpuHighPercent = request.AlertCpuHighPercent;
            setting.AlertMemoryHighEnabled = request.AlertMemoryHighEnabled;
            setting.AlertMemoryHighPercent = request.AlertMemoryHighPercent;
            setting.UpdatedAt = DateTimeOffset.UtcNow;

            db.SaveChanges();

            auditLog.Log(http, "settings.update", "ServerSetting", "1", new
            {
                request.ServerUrl,
                request.HeartbeatSeconds,
                request.DefaultLocationCode,
                request.AlertsEnabled,
                request.AlertOfflineEnabled,
                request.AlertDiskLowEnabled,
                request.AlertCpuHighEnabled,
                request.AlertMemoryHighEnabled,
                request.SmtpSslMode,
                SmtpConfigured = !string.IsNullOrWhiteSpace(request.SmtpHost)
            });

            return Results.Ok(new ServerSettingsContract(
                setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode,
                SmtpHost: setting.SmtpHost, SmtpPort: setting.SmtpPort, SmtpUsername: setting.SmtpUsername,
                SmtpPassword: null, SmtpFromAddress: setting.SmtpFromAddress, SmtpFromName: setting.SmtpFromName,
                SmtpSslMode: setting.SmtpSslMode ?? "Auto",
                AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
                AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
                AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
                AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
                AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
        });

        admin.MapPost("/admin/settings/smtp/test", async (
            SmtpTestRequest request,
            HttpContext http,
            EmailService email,
            AuditLogService auditLog) =>
        {
            if (string.IsNullOrWhiteSpace(request.ToEmail))
            {
                return Results.BadRequest(new { message = "Geçerli bir alıcı e-posta adresi belirtilmelidir." });
            }

            var (success, error) = await email.SendTestAsync(request);

            auditLog.Log(http, "settings.smtp_test", "ServerSetting", "1", new { toEmail = request.ToEmail, success, error }, success);

            return success ? Results.Ok(new { message = "Test e-postası başarıyla gönderildi." }) : Results.BadRequest(new { message = error });
        });

        // Veritabanı Yedekleme ve Bakım Endpoint'leri (Madde 6)
        admin.MapGet("/admin/database/backups", (DatabaseMaintenanceService maintenance) =>
        {
            return Results.Ok(maintenance.GetBackups());
        });

        admin.MapPost("/admin/database/backup", async (
            HttpContext http,
            DatabaseMaintenanceService maintenance,
            AuditLogService auditLog,
            CancellationToken ct) =>
        {
            var result = await maintenance.CreateBackupAsync(ct);
            if (result.Success)
            {
                auditLog.Log(http, "database.backup", "Database", result.BackupFileName, new { result.BackupFileName, result.SizeBytes });
                return Results.Ok(result);
            }

            auditLog.Log(http, "database.backup", "Database", "failed", new { result.ErrorMessage }, success: false);
            return Results.BadRequest(new { message = result.ErrorMessage ?? "Yedekleme oluşturulamadı." });
        });

        admin.MapPost("/admin/database/maintenance", async (
            HttpContext http,
            DatabaseMaintenanceService maintenance,
            AuditLogService auditLog,
            CancellationToken ct) =>
        {
            var result = await maintenance.PurgeExpiredDataAsync(ct);
            auditLog.Log(http, "database.maintenance", "Database", "retention_purge", result);
            return Results.Ok(result);
        });
    }
}
