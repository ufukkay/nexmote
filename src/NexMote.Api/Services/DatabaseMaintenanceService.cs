using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexMote.Api.Data;

namespace NexMote.Api.Services;

public sealed record DatabaseBackupInfo(
    string FileName,
    string FullPath,
    long SizeBytes,
    DateTimeOffset CreatedAt);

public sealed record DatabaseBackupResult(
    bool Success,
    string? BackupFileName,
    long? SizeBytes,
    string? ErrorMessage);

public sealed record DatabasePurgeResult(
    int PurgedSessions,
    int PurgedCommands,
    int PurgedAlerts,
    int PurgedAudits,
    int PurgedLogs);

/// <summary>
/// Arka planda periyodik olarak çalışan veritabanı bakım, veri saklama (retention)
/// ve canlı SQLite sıfır-kesintili nokta-zamanlı (point-in-time) yedekleme servisi.
/// </summary>
public sealed class DatabaseMaintenanceService : BackgroundService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<DatabaseMaintenanceService> _logger;
    private readonly string _backupDirectory;
    private const int MaxBackupsToRetain = 7;

    public DatabaseMaintenanceService(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<DatabaseMaintenanceService> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _backupDirectory = Path.Combine(AppContext.BaseDirectory, "backups");
        if (!Directory.Exists(_backupDirectory))
        {
            Directory.CreateDirectory(_backupDirectory);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Başlangıçta uygulamanın tamamen ayağa kalkması için 30 saniye bekle
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Periyodik veritabanı bakım ve yedekleme döngüsü başlatılıyor...");

                // 1. Veri saklama (Retention) temizliği
                var purgeResult = await PurgeExpiredDataAsync(stoppingToken);
                _logger.LogInformation(
                    "Veri temizliği tamamlandı: {Sessions} oturum, {Commands} kuyruk komutu, {Alerts} uyarı, {Audits} denetim, {Logs} aktivite kaydı temizlendi.",
                    purgeResult.PurgedSessions, purgeResult.PurgedCommands, purgeResult.PurgedAlerts, purgeResult.PurgedAudits, purgeResult.PurgedLogs);

                // 2. Canlı veritabanı yedeği al
                var backupResult = await CreateBackupAsync(stoppingToken);
                if (backupResult.Success)
                {
                    _logger.LogInformation("Veritabanı yedeği başarıyla alındı: {FileName} ({Bytes:N0} byte)",
                        backupResult.BackupFileName, backupResult.SizeBytes);
                }
                else
                {
                    _logger.LogWarning("Veritabanı yedeği alınırken hata: {Error}", backupResult.ErrorMessage);
                }

                // 3. Eski yedek dosyalarını temizle (En fazla 7 adet tut)
                RotateOldBackups();
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Veritabanı bakım döngüsü sırasında beklenmeyen hata.");
            }

            // Bir sonraki bakıma kadar 24 saat bekle
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    /// <summary>
    /// SQLite VACUUM INTO komutuyla çalışan veritabanını kilitlemeden atomik, bozulmasız anlık yedek alır.
    /// </summary>
    public async Task<DatabaseBackupResult> CreateBackupAsync(CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_backupDirectory))
            {
                Directory.CreateDirectory(_backupDirectory);
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            var backupFileName = $"nexmote-backup-{timestamp}.db";
            var backupPath = Path.Combine(_backupDirectory, backupFileName).Replace('\\', '/');

            using var db = _dbFactory.CreateDbContext();
#pragma warning disable EF1002 // VACUUM INTO SQLite sözdiziminde yol parametre olarak verilemez, sunucu tarafında temizlenmiş güvenli yoldur
            await db.Database.ExecuteSqlRawAsync($"VACUUM INTO '{backupPath}';", ct);
#pragma warning restore EF1002

            var fileInfo = new FileInfo(backupPath);
            RotateOldBackups();

            return new DatabaseBackupResult(true, backupFileName, fileInfo.Length, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VACUUM INTO yedekleme işlemi başarısız oldu.");
            return new DatabaseBackupResult(false, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Veri saklama politikalarına göre süresi dolmuş eski kayıtları temizler.
    /// </summary>
    public async Task<DatabasePurgeResult> PurgeExpiredDataAsync(CancellationToken ct = default)
    {
        using var db = _dbFactory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;

        // 1. Süresi 14 günden önce dolmuş veya 14 günden önce iptal edilmiş kullanıcı oturumları
        var sessionCutoff = now.AddDays(-14);
        var oldSessions = await db.UserSessions
            .Where(s => s.ExpiresAt < sessionCutoff || (s.RevokedAt != null && s.RevokedAt < sessionCutoff))
            .ToListAsync(ct);
        db.UserSessions.RemoveRange(oldSessions);

        // 2. Tamamlanmış ve 14 günden eski kuyruk komutları
        var commandCutoff = now.AddDays(-14);
        var oldCommands = await db.DeviceCommands
            .Where(c => (c.CompletedAt != null && c.CompletedAt < commandCutoff) || (c.CreatedAt < now.AddDays(-30)))
            .ToListAsync(ct);
        db.DeviceCommands.RemoveRange(oldCommands);

        // 3. Çözülmüş ve 30 günden eski cihaz uyarıları
        var alertCutoff = now.AddDays(-30);
        var oldAlerts = await db.DeviceAlerts
            .Where(a => a.ResolvedAt != null && a.ResolvedAt < alertCutoff)
            .ToListAsync(ct);
        db.DeviceAlerts.RemoveRange(oldAlerts);

        // 4. 90 günden eski terminal komut denetim kayıtları
        var auditCutoff = now.AddDays(-90);
        var oldAudits = await db.CommandAudits
            .Where(a => a.ExecutedAt < auditCutoff)
            .ToListAsync(ct);
        db.CommandAudits.RemoveRange(oldAudits);

        // 5. 180 günden eski aktivite logları
        var logCutoff = now.AddDays(-180);
        var oldLogs = await db.ActivityLogs
            .Where(l => l.CreatedAt < logCutoff)
            .ToListAsync(ct);
        db.ActivityLogs.RemoveRange(oldLogs);

        await db.SaveChangesAsync(ct);

        return new DatabasePurgeResult(
            oldSessions.Count,
            oldCommands.Count,
            oldAlerts.Count,
            oldAudits.Count,
            oldLogs.Count);
    }

    /// <summary>
    /// Mevcut veritabanı yedeklerini listeler.
    /// </summary>
    public IReadOnlyList<DatabaseBackupInfo> GetBackups()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            return Array.Empty<DatabaseBackupInfo>();
        }

        var dir = new DirectoryInfo(_backupDirectory);
        return dir.GetFiles("nexmote-backup-*.db")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new DatabaseBackupInfo(f.Name, f.FullName, f.Length, f.CreationTimeUtc))
            .ToList();
    }

    private void RotateOldBackups()
    {
        try
        {
            if (!Directory.Exists(_backupDirectory)) return;

            var dir = new DirectoryInfo(_backupDirectory);
            var backups = dir.GetFiles("nexmote-backup-*.db")
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(MaxBackupsToRetain)
                .ToList();

            foreach (var old in backups)
            {
                try
                {
                    old.Delete();
                    _logger.LogInformation("Eski veritabanı yedeği silindi: {FileName}", old.Name);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Eski yedekler rotasyon edilirken hata oluştu.");
        }
    }
}
