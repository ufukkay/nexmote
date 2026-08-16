using System.Security.Cryptography;
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
    /// </summary>
    /// <param name="request">Agent tarafından gönderilen kayıt bilgileri.</param>
    /// <returns>Cihaz ID'si ve güvenlik token'ını içeren yanıt.</returns>
    public AgentEnrollmentResponse Enroll(AgentEnrollmentRequest request)
    {
        using var db = _dbFactory.CreateDbContext();

        // Aynı bilgisayar adı ve domain'e sahip mevcut kaydı kontrol et
        var existing = db.Devices.FirstOrDefault(device =>
            device.DeviceName.ToLower() == request.DeviceName.ToLower() &&
            device.DomainName.ToLower() == request.DomainName.ToLower());

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;

        if (existing is null)
        {
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
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="request">Heartbeat verisi ve donanım metrikleri.</param>
    /// <returns>Token doğruysa ve cihaz güncellendiyse true; aksi halde false.</returns>
    public bool Heartbeat(Guid deviceId, DeviceHeartbeatRequest request)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device is null || device.AgentToken != request.AgentToken)
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
    /// Belirtilen cihaz kimliği ve agent token'ının doğruluğunu kontrol eder.
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="agentToken">Doğrulanacak token.</param>
    public bool ValidateAgent(Guid deviceId, string agentToken)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
        return device is not null && string.Equals(device.AgentToken, agentToken, StringComparison.Ordinal);
    }

    /// <summary>
    /// Veritabanı DeviceEntity nesnesini DTO olan DeviceSummary nesnesine dönüştürür.
    /// </summary>
    private static DeviceSummary ToSummary(DeviceEntity device)
    {
        return new DeviceSummary(
            device.Id,
            device.DeviceName,
            device.DomainName,
            device.OperatingSystem ?? "Windows",
            device.AgentVersion ?? "1.0.0",
            device.ActiveUser,
            device.IpAddress,
            device.LocationCode,
            DateTimeOffset.UtcNow - device.LastSeenAt < TimeSpan.FromMinutes(2),
            device.LastSeenAt,
            device.CpuUsagePercent,
            device.MemoryTotalMb,
            device.MemoryUsedMb,
            device.DiskFreeMb);
    }
}
