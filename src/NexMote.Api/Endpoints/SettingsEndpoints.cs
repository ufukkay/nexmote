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

        app.MapGet("/api/updates/check", (IConfiguration config, DownloadCatalog downloads) =>
        {
            var baseUrl = config["PublicUrl"] ?? "https://nexmote.com";
            var versions = downloads.GetVersionInfo();
            return Results.Ok(new
            {
                agent = new
                {
                    version = versions.Agent.Version,
                    downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Agent-Setup.msi",
                    releaseNotes = versions.Agent.ReleaseNotes
                },
                technician = new
                {
                    version = versions.Technician.Version,
                    downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Technician-Setup.msi",
                    releaseNotes = versions.Technician.ReleaseNotes
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
                AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
                AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
                AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
                AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
                AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
        });

        admin.MapPost("/settings", (ServerSettingsContract request, IDbContextFactory<AppDbContext> dbFactory, EmailService email) =>
        {
            using var db = dbFactory.CreateDbContext();
            var setting = db.ServerSettings.First();
            setting.ServerUrl = request.ServerUrl.TrimEnd('/');
            setting.EnrollmentKey = request.EnrollmentKey;
            setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
            setting.DefaultLocationCode = request.DefaultLocationCode;
            setting.SmtpHost = request.SmtpHost;
            setting.SmtpPort = request.SmtpPort <= 0 ? 465 : request.SmtpPort;
            setting.SmtpUsername = request.SmtpUsername;
            setting.SmtpFromAddress = request.SmtpFromAddress;
            setting.SmtpFromName = request.SmtpFromName;
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
            return Results.Ok(new ServerSettingsContract(
                setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode,
                SmtpHost: setting.SmtpHost, SmtpPort: setting.SmtpPort, SmtpUsername: setting.SmtpUsername,
                SmtpPassword: null, SmtpFromAddress: setting.SmtpFromAddress, SmtpFromName: setting.SmtpFromName,
                AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
                AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
                AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
                AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
                AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
        });

        admin.MapPost("/admin/settings/smtp/test", async (SmtpTestRequest request, EmailService email) =>
        {
            var (success, error) = await email.SendAsync(
                request.ToEmail,
                "NexMote - Test E-postası",
                "<p>Bu, NexMote sunucunuzun SMTP yapılandırmasını doğrulamak için gönderilen bir test e-postasıdır.</p>");
            return success ? Results.Ok(new { message = "Test e-postası gönderildi." }) : Results.BadRequest(new { message = error });
        });
    }
}
