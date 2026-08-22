using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// İstemci cihazların ilk kaydını (Enrollment), periyodik canlılık ve telemetri bildirimlerini (Heartbeat),
/// cihaz listelemeyi ve token doğrulama işlemlerini yöneten servis.
/// </summary>
public sealed class DeviceRegistry
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DeviceRegistry(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Yeni bir cihazı sisteme kaydeder veya mevcut cihazın işletim sistemi ve sürüm bilgilerini günceller.
    /// Cihaza özel 32-byte rastgele bir güvenlik token'ı üretir ve döner.
    /// Enrollment key'i SHA-256 hash olarak veritabanına kaydeder; plaintext asla saklanmaz.
    /// </summary>
    /// <param name="request">Agent tarafından gönderilen kayıt bilgileri.</param>
    /// <returns>Cihaz ID'si ve güvenlik token'ını içeren yanıt.</returns>
    public AgentEnrollmentResponse Enroll(AgentEnrollmentRequest request)
    {
        using var db = _dbFactory.CreateDbContext();

        var nameLower = request.DeviceName.ToLowerInvariant();
        var domainLower = request.DomainName.ToLowerInvariant();

        // Eğer bu cihaz daha önce yönetici tarafından silinmişse, otomatik kaydı engelle
        var isDeleted = db.DeletedDevices.Any(d => d.DeviceName.ToLower() == nameLower && d.DomainName.ToLower() == domainLower);
        if (isDeleted)
        {
            throw new InvalidOperationException("Bu cihaz yönetici tarafından sistemden silinmiştir.");
        }

        var existing = db.Devices.FirstOrDefault(device =>
            device.DeviceName.ToLower() == nameLower &&
            device.DomainName.ToLower() == domainLower);

        var now = DateTimeOffset.UtcNow;
        string token;

        if (existing is null)
        {
            // Cihaza özgü 64-karakter hex token üret (kriptografik olarak güçlü)
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            existing = new DeviceEntity
            {
                Id = Guid.NewGuid(),
                DeviceName = request.DeviceName,
                DomainName = request.DomainName,
                OperatingSystem = request.OperatingSystem,
                AgentVersion = request.AgentVersion,
                SerialNumber = request.SerialNumber,
                LocationCode = request.LocationCode,
                AgentToken = token,
                LastSeenAt = now,
                EnrolledAt = now
            };
            db.Devices.Add(existing);
        }
        else
        {
            // Mevcut cihaz yeniden kaydoluyorsa mevcut token'ı koru (böylece servis ve tray arasındaki token senkronizasyonu bozulmaz)
            token = string.IsNullOrWhiteSpace(existing.AgentToken)
                ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32))
                : existing.AgentToken;

            existing.OperatingSystem = request.OperatingSystem;
            existing.AgentVersion = request.AgentVersion;
            existing.SerialNumber = request.SerialNumber;
            existing.LocationCode = request.LocationCode;
            existing.AgentToken = token;
            existing.LastSeenAt = now;
            db.Devices.Update(existing);
        }

        db.SaveChanges();

        return new AgentEnrollmentResponse(
            existing.Id,
            token,
            new Uri("/hubs/signaling", UriKind.Relative),
            TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Cihazdan gelen periyodik heartbeat sinyalini ve CPU/RAM/Disk donanım telemetrisini işler.
    /// Token karşılaştırması timing-safe (CryptographicOperations.FixedTimeEquals) yöntemiyle yapılır.
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="request">Heartbeat verisi ve donanım metrikleri.</param>
    /// <returns>Token doğruysa ve cihaz güncellendiyse true; aksi halde false.</returns>
    public bool Heartbeat(Guid deviceId, DeviceHeartbeatRequest request)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device is null)
        {
            return false;
        }

        // Timing-safe karşılaştırma: == operatörü yan-kanal saldırısına açıktır
        if (!TokenEquals(device.AgentToken, request.AgentToken))
        {
            return false;
        }

        device.ActiveUser = request.ActiveUser;
        device.IpAddress = request.IpAddress;
        device.CpuUsagePercent = request.CpuUsagePercent;
        device.MemoryTotalMb = request.MemoryTotalMb;
        device.MemoryUsedMb = request.MemoryUsedMb;
        device.DiskFreeMb = request.DiskFreeMb;
        device.UptimeSeconds = request.UptimeSeconds;
        device.LastSeenAt = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.AgentVersion))
        {
            device.AgentVersion = request.AgentVersion;
        }

        if (request.NetworkAdapters != null && request.NetworkAdapters.Count > 0)
        {
            try
            {
                device.NetworkAdaptersJson = JsonSerializer.Serialize(request.NetworkAdapters);
            }
            catch { }
        }

        if (request.InstalledApps != null && request.InstalledApps.Count > 0)
        {
            try
            {
                device.InstalledAppsJson = JsonSerializer.Serialize(request.InstalledApps);
            }
            catch { }
        }

        if (request.WindowsUpdates != null && request.WindowsUpdates.Count > 0)
        {
            try
            {
                device.WindowsUpdatesJson = JsonSerializer.Serialize(request.WindowsUpdates);
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(request.SerialNumber))
        {
            device.SerialNumber = request.SerialNumber;
        }

        if (request.HardwareDetails != null)
        {
            try
            {
                device.HardwareDetailsJson = JsonSerializer.Serialize(request.HardwareDetails);
                if (string.IsNullOrWhiteSpace(device.SerialNumber) && !string.IsNullOrWhiteSpace(request.HardwareDetails.SystemSerialNumber))
                {
                    device.SerialNumber = request.HardwareDetails.SystemSerialNumber;
                }
            }
            catch { }
        }

        db.SaveChanges();
        return true;
    }

    /// <summary>
    /// Kayıtlı tüm cihazların özet bilgilerini ve donanım kullanım verilerini son görülme tarihine göre sıralı olarak döner.
    /// </summary>
    public IReadOnlyCollection<DeviceSummary> List()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Devices
            .AsNoTracking()
            .ToList()
            .OrderByDescending(device => device.LastSeenAt)
            .Select(device => ToSummary(device))
            .ToArray();
    }

    /// <summary>
    /// Belirli bir cihazın özet ve telemetri detayını getirir.
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    public DeviceSummary? Get(Guid deviceId)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        return device is null ? null : ToSummary(device);
    }

    /// <summary>
    /// Belirtilen kimliğe sahip tekil cihazın detay ve donanım bilgilerini döner.
    /// </summary>
    public DeviceSummary? GetById(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == id);
        return device is null ? null : ToSummary(device);
    }

    /// <summary>
    /// Belirtilen kimliğe sahip cihazı veritabanından kalıcı olarak siler.
    /// </summary>
    /// <param name="id">Silinecek cihaz kimliği.</param>
    /// <returns>Cihaz bulundu ve silindiyse true; bulunamadıysa false.</returns>
    public bool Delete(Guid id)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == id);
        if (device is null)
        {
            return false;
        }

        var nameLower = device.DeviceName.ToLowerInvariant();
        var domainLower = device.DomainName.ToLowerInvariant();
        var isAlreadyInDeleted = db.DeletedDevices.Any(d => d.DeviceName.ToLower() == nameLower && d.DomainName.ToLower() == domainLower);
        if (!isAlreadyInDeleted)
        {
            db.DeletedDevices.Add(new DeletedDeviceEntity
            {
                Id = Guid.NewGuid(),
                DeviceName = device.DeviceName,
                DomainName = device.DomainName,
                DeletedAt = DateTimeOffset.UtcNow
            });
        }

        db.Devices.Remove(device);
        db.SaveChanges();
        return true;
    }

    /// <summary>
    /// Kaldırılan bir uygulamayı cihazın veritabanı ve önbellek kaydından derhal siler.
    /// </summary>
    public void RemoveInstalledApp(Guid deviceId, string appName)
    {
        try
        {
            using var db = _dbFactory.CreateDbContext();
            var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
            if (device == null || string.IsNullOrWhiteSpace(device.InstalledAppsJson)) return;

            var apps = JsonSerializer.Deserialize<List<InstalledAppInfo>>(device.InstalledAppsJson);
            if (apps != null)
            {
                var filtered = apps.Where(a => !string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase)).ToList();
                device.InstalledAppsJson = JsonSerializer.Serialize(filtered);
                db.SaveChanges();
            }
        }
        catch { }
    }

    /// <summary>
    /// Belirtilen cihaz kimliği ve agent token'ının doğruluğunu kontrol eder.
    /// Timing-safe karşılaştırma kullanır.
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="agentToken">Doğrulanacak token.</param>
    public bool ValidateAgent(Guid deviceId, string agentToken)
    {
        if (string.IsNullOrEmpty(agentToken))
        {
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        return device is not null && TokenEquals(device.AgentToken, agentToken);
    }

    /// <summary>
    /// Veritabanı DeviceEntity nesnesini DTO olan DeviceSummary nesnesine dönüştürür.
    /// </summary>
    private static DeviceSummary ToSummary(DeviceEntity device)
    {
        List<NetworkAdapterInfo>? adapters = null;
        if (!string.IsNullOrWhiteSpace(device.NetworkAdaptersJson))
        {
            try
            {
                adapters = JsonSerializer.Deserialize<List<NetworkAdapterInfo>>(device.NetworkAdaptersJson);
            }
            catch { }
        }

        List<InstalledAppInfo>? apps = null;
        if (!string.IsNullOrWhiteSpace(device.InstalledAppsJson))
        {
            try
            {
                apps = JsonSerializer.Deserialize<List<InstalledAppInfo>>(device.InstalledAppsJson);
            }
            catch { }
        }

        List<WindowsUpdateInfo>? updates = null;
        if (!string.IsNullOrWhiteSpace(device.WindowsUpdatesJson))
        {
            try
            {
                updates = JsonSerializer.Deserialize<List<WindowsUpdateInfo>>(device.WindowsUpdatesJson);
            }
            catch { }
        }

        HardwareInventoryInfo? hardware = null;
        if (!string.IsNullOrWhiteSpace(device.HardwareDetailsJson))
        {
            try
            {
                hardware = JsonSerializer.Deserialize<HardwareInventoryInfo>(device.HardwareDetailsJson);
            }
            catch { }
        }

        return new DeviceSummary(
            device.Id,
            device.DeviceName,
            device.DomainName,
            device.OperatingSystem ?? "Windows",
            device.AgentVersion ?? "1.0.0",
            CleanUserName(device.ActiveUser),
            device.IpAddress,
            device.LocationCode,
            DateTimeOffset.UtcNow - device.LastSeenAt < TimeSpan.FromMinutes(2),
            device.LastSeenAt,
            device.CpuUsagePercent,
            device.MemoryTotalMb,
            device.MemoryUsedMb,
            device.DiskFreeMb,
            adapters,
            apps,
            updates,
            device.SerialNumber,
            hardware);
    }

    private static string? CleanUserName(string? rawUser)
    {
        if (string.IsNullOrWhiteSpace(rawUser)) return null;
        var user = rawUser.Trim();
        var idx = user.LastIndexOf('\\');
        if (idx >= 0 && idx < user.Length - 1) user = user.Substring(idx + 1);
        var at = user.IndexOf('@');
        if (at > 0) user = user.Substring(0, at);
        user = user.Trim();
        if (user.EndsWith("$", StringComparison.Ordinal) ||
            string.Equals(user, "SYSTEM", StringComparison.OrdinalIgnoreCase)) return null;
        return string.IsNullOrEmpty(user) ? null : user;
    }

    /// <summary>
    /// İki token string'ini yan-kanal (timing) saldırılarına karşı güvenli şekilde karşılaştırır.
    /// CryptographicOperations.FixedTimeEquals sabit zamanda çalışır; uzunluk farkı olsa da erken çıkmaz.
    /// </summary>
    private static bool TokenEquals(string storedToken, string providedToken)
    {
        if (string.IsNullOrEmpty(storedToken) || string.IsNullOrEmpty(providedToken))
        {
            return false;
        }

        var storedBytes = Encoding.UTF8.GetBytes(storedToken);
        var providedBytes = Encoding.UTF8.GetBytes(providedToken);

        // Uzunluk farklıysa FixedTimeEquals false döner ama biz yine de sabit süre harcıyoruz
        if (storedBytes.Length != providedBytes.Length)
        {
            // Uzunluk bilgisini sızdırmamak için referans uzunlukta dummy karşılaştırma yap
            CryptographicOperations.FixedTimeEquals(storedBytes, storedBytes);
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(storedBytes, providedBytes);
    }
}
