using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Cihaz bazlı proaktif uyarıları (çevrimdışı, disk/CPU/RAM eşik aşımı) değerlendirir, durumlarını
/// <see cref="DeviceAlertEntity"/> tablosunda takip eder ve <see cref="EmailService"/> ile e-posta gönderir.
/// Değerlendirme <see cref="AlertMonitorService"/> tarafından periyodik olarak tetiklenir.
/// </summary>
public sealed class AlertService
{
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromHours(4);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly EmailService _email;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IDbContextFactory<AppDbContext> dbFactory, EmailService email, ILogger<AlertService> logger)
    {
        _dbFactory = dbFactory;
        _email = email;
        _logger = logger;
    }

    /// <summary>Şu an açık (çözülmemiş) tüm uyarıları döner — web konsolunun "Dikkat" filtresi ve cihaz detay rozeti için.</summary>
    public IReadOnlyList<ActiveDeviceAlert> ListActive()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.DeviceAlerts.AsNoTracking()
            .Where(a => a.ResolvedAt == null)
            .Select(a => new ActiveDeviceAlert(a.DeviceId, a.AlertType, a.TriggeredAt))
            .ToList();
    }

    /// <summary>Tüm cihazları tarar, eşikleri kontrol eder, açık uyarıları günceller ve gerektiğinde e-posta gönderir.</summary>
    public async Task EvaluateAndNotifyAsync(CancellationToken cancellationToken = default)
    {
        using var db = _dbFactory.CreateDbContext();
        var settings = db.ServerSettings.AsNoTracking().First();
        if (!settings.AlertsEnabled)
        {
            return;
        }

        var recipients = ResolveRecipients(db, settings);
        var now = DateTimeOffset.UtcNow;
        var devices = db.Devices.AsNoTracking().ToList();
        var openAlerts = db.DeviceAlerts
            .Where(a => a.ResolvedAt == null)
            .ToDictionary(a => (a.DeviceId, a.AlertType));

        foreach (var device in devices)
        {
            foreach (var rule in BuildRules(settings))
            {
                var key = (device.Id, rule.Type);
                var isTriggered = rule.Enabled && rule.IsTriggered(device, now, settings);
                openAlerts.TryGetValue(key, out var existing);

                if (isTriggered)
                {
                    if (existing is null)
                    {
                        var alert = new DeviceAlertEntity
                        {
                            Id = Guid.NewGuid(),
                            DeviceId = device.Id,
                            AlertType = rule.Type,
                            TriggeredAt = now,
                            LastNotifiedAt = now
                        };
                        db.DeviceAlerts.Add(alert);
                        await SendAlertEmailAsync(recipients, device, rule, resolved: false);
                    }
                    else if (now - existing.LastNotifiedAt > ReminderInterval)
                    {
                        existing.LastNotifiedAt = now;
                        await SendAlertEmailAsync(recipients, device, rule, resolved: false);
                    }
                }
                else if (existing is not null)
                {
                    existing.ResolvedAt = now;
                    await SendAlertEmailAsync(recipients, device, rule, resolved: true);
                }
            }
        }

        db.SaveChanges();
    }

    private static List<string> ResolveRecipients(AppDbContext db, ServerSettingEntity settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AlertRecipientEmails))
        {
            return settings.AlertRecipientEmails
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Role == UserRoles.Admin)
            .Select(u => u.Email)
            .ToList();
    }

    private async Task SendAlertEmailAsync(List<string> recipients, DeviceEntity device, AlertRule rule, bool resolved)
    {
        if (recipients.Count == 0)
        {
            return;
        }

        var (subject, body) = rule.Describe(device, resolved);
        foreach (var recipient in recipients)
        {
            var (success, error) = await _email.SendAsync(recipient, subject, body);
            if (!success)
            {
                _logger.LogWarning("Uyarı e-postası gönderilemedi ({Recipient}, {AlertType}): {Error}", recipient, rule.Type, error);
            }
        }
    }

    private static IEnumerable<AlertRule> BuildRules(ServerSettingEntity s) => new[]
    {
        new AlertRule(
            "Offline",
            s.AlertOfflineEnabled,
            (device, now, settings) => now - device.LastSeenAt > TimeSpan.FromMinutes(settings.AlertOfflineMinutes),
            (device, resolved) => resolved
                ? ($"✅ [NexMote] {device.DeviceName} tekrar çevrimiçi",
                   $"<p><strong>{device.DeviceName}</strong> cihazı tekrar sunucuya sinyal göndermeye başladı.</p>")
                : ($"🔴 [NexMote] {device.DeviceName} çevrimdışı",
                   $"<p><strong>{device.DeviceName}</strong> cihazı {s.AlertOfflineMinutes} dakikadan uzun süredir sunucuya sinyal göndermiyor.</p>" +
                   $"<p>Son görülme: {device.LastSeenAt:dd.MM.yyyy HH:mm} (UTC)</p>")),

        new AlertRule(
            "DiskLow",
            s.AlertDiskLowEnabled,
            (device, now, settings) => device.MemoryTotalMb > 0 && device.DiskFreeMb < settings.AlertDiskLowMb,
            (device, resolved) => resolved
                ? ($"✅ [NexMote] {device.DeviceName} disk alanı düzeldi",
                   $"<p><strong>{device.DeviceName}</strong> cihazında boş disk alanı tekrar eşik değerinin üzerine çıktı ({device.DiskFreeMb} MB).</p>")
                : ($"🟠 [NexMote] {device.DeviceName} disk alanı azaldı",
                   $"<p><strong>{device.DeviceName}</strong> cihazında boş disk alanı <strong>{device.DiskFreeMb} MB</strong>'a düştü (eşik: {s.AlertDiskLowMb} MB).</p>")),

        new AlertRule(
            "CpuHigh",
            s.AlertCpuHighEnabled,
            (device, now, settings) => device.MemoryTotalMb > 0 && device.CpuUsagePercent > settings.AlertCpuHighPercent,
            (device, resolved) => resolved
                ? ($"✅ [NexMote] {device.DeviceName} CPU kullanımı normale döndü",
                   $"<p><strong>{device.DeviceName}</strong> cihazında CPU kullanımı tekrar eşik değerinin altına düştü (%{device.CpuUsagePercent:F0}).</p>")
                : ($"🟠 [NexMote] {device.DeviceName} CPU kullanımı yüksek",
                   $"<p><strong>{device.DeviceName}</strong> cihazında CPU kullanımı <strong>%{device.CpuUsagePercent:F0}</strong> (eşik: %{s.AlertCpuHighPercent:F0}).</p>")),

        new AlertRule(
            "MemoryHigh",
            s.AlertMemoryHighEnabled,
            (device, now, settings) => device.MemoryTotalMb > 0 &&
                (100.0 * device.MemoryUsedMb / device.MemoryTotalMb) > settings.AlertMemoryHighPercent,
            (device, resolved) =>
            {
                var pct = device.MemoryTotalMb > 0 ? 100.0 * device.MemoryUsedMb / device.MemoryTotalMb : 0;
                return resolved
                    ? ($"✅ [NexMote] {device.DeviceName} RAM kullanımı normale döndü",
                       $"<p><strong>{device.DeviceName}</strong> cihazında RAM kullanımı tekrar eşik değerinin altına düştü (%{pct:F0}).</p>")
                    : ($"🟠 [NexMote] {device.DeviceName} RAM kullanımı yüksek",
                       $"<p><strong>{device.DeviceName}</strong> cihazında RAM kullanımı <strong>%{pct:F0}</strong> (eşik: %{s.AlertMemoryHighPercent:F0}).</p>");
            })
    };

    private sealed record AlertRule(
        string Type,
        bool Enabled,
        Func<DeviceEntity, DateTimeOffset, ServerSettingEntity, bool> IsTriggered,
        Func<DeviceEntity, bool, (string Subject, string Body)> Describe);
}
