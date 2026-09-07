using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
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
    /// <param name="groupId">Sunulan kayıt anahtarı bir Cihaz Grubuna özelse, cihazın İLK kayıtta otomatik atanacağı grup. Zaten var olan bir cihazın grubu bu yolla değiştirilmez (admin'in elle atadığı grup korunur).</param>
    /// <returns>Cihaz ID'si ve güvenlik token'ını içeren yanıt.</returns>
    public AgentEnrollmentResponse Enroll(AgentEnrollmentRequest request, Guid? groupId = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var nameLower = request.DeviceName.Trim().ToLower();
        var domainLower = request.DomainName.Trim().ToLower();
        var serial = request.SerialNumber?.Trim();

        // Eğer bu cihaz daha önce DeletedDevices listesindeyse, yeni kurulumla kaydolurken bu engeli kaldır
        var deletedEntries = db.DeletedDevices
            .Where(d => d.DeviceName.ToLower() == nameLower &&
                       (d.DomainName.ToLower() == domainLower ||
                        d.DomainName.ToLower() == "workgroup" ||
                        domainLower == "workgroup" ||
                        d.DomainName.ToLower() == nameLower ||
                        domainLower == nameLower))
            .ToList();
        if (deletedEntries.Count > 0)
        {
            db.DeletedDevices.RemoveRange(deletedEntries);
        }

        // 1. Önce tam eşleşme (DeviceName ve DomainName birebir aynı)
        var existing = db.Devices.FirstOrDefault(device =>
            device.DeviceName.ToLower() == nameLower &&
            device.DomainName.ToLower() == domainLower);

        // 2. Seri numarasıyla eşleşme (Donanım seri numarası varsa ve jenerik değilse aynı fiziksel makinedir)
        if (existing is null && !string.IsNullOrWhiteSpace(serial) && !IsGenericSerial(serial))
        {
            existing = db.Devices.FirstOrDefault(device =>
                device.SerialNumber != null &&
                device.SerialNumber.ToLower() == serial.ToLower());
        }

        // 3. Bilgisayar adı (DeviceName) esnek eşleşmesi:
        //    Domainlerden biri WORKGROUP ise veya Domain adı bilgisayar adına eşitse (yerel workgroup oturumu)
        //    mükerrer oluşturma; var olan kaydı güncelle.
        if (existing is null)
        {
            existing = db.Devices.FirstOrDefault(device =>
                device.DeviceName.ToLower() == nameLower &&
                (device.DomainName.ToLower() == domainLower ||
                 device.DomainName.ToLower() == "workgroup" ||
                 domainLower == "workgroup" ||
                 device.DomainName.ToLower() == nameLower ||
                 domainLower == nameLower));
        }

        var now = DateTimeOffset.UtcNow;
        string rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        string tokenHash = SessionTokens.Hash(rawToken);

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
                AgentToken = tokenHash, // DB'de yalnızca SHA-256 hash saklanır
                LastSeenAt = now,
                EnrolledAt = now,
                GroupId = groupId
            };
            db.Devices.Add(existing);
        }
        else
        {
            // Yeniden kayıt, yalnızca bilgisayar adı/domain bilgisine güvenerek mevcut token'ı geri vermemeli.
            // Bu yüzden mevcut cihaz kaydı korunur ama agent token'ı rotate edilir.
            existing.OperatingSystem = request.OperatingSystem;
            existing.AgentVersion = request.AgentVersion;
            if (!string.IsNullOrWhiteSpace(request.SerialNumber))
            {
                existing.SerialNumber = request.SerialNumber;
            }
            if (!string.IsNullOrWhiteSpace(request.LocationCode))
            {
                existing.LocationCode = request.LocationCode;
            }
            // Eğer yeni gelen Domain adı gerçek bir kurumsal domain ise (WORKGROUP veya bilgisayar adı değilse) domain'i güncelle
            if (domainLower != "workgroup" && domainLower != nameLower)
            {
                existing.DomainName = request.DomainName;
            }
            existing.AgentToken = tokenHash;
            existing.LastSeenAt = now;
            db.Devices.Update(existing);
        }

        db.SaveChanges();

        return new AgentEnrollmentResponse(
            existing.Id,
            rawToken,
            new Uri("/hubs/signaling", UriKind.Relative),
            TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// Cihazdan gelen periyodik heartbeat sinyalini ve CPU/RAM/Disk donanım telemetrisini işler.
    /// Token karşılaştırması timing-safe (CryptographicOperations.FixedTimeEquals) yöntemiyle yapılır.
    /// DB'de SHA-256 hash'lenmiş token kontrol edilir; eski düz metin token varsa otomatik hash'e yükseltilir.
    /// </summary>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="request">Heartbeat verisi ve donanım metrikleri.</param>
    /// <returns>Token doğruysa ve cihaz güncellendiyse true; aksi halde false.</returns>
    public bool Heartbeat(Guid deviceId, DeviceHeartbeatRequest request)
    {
        using var db = _dbFactory.CreateDbContext();
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);

        if (device is null || string.IsNullOrWhiteSpace(request.AgentToken))
        {
            return false;
        }

        var providedHash = SessionTokens.Hash(request.AgentToken);
        if (TokenEquals(device.AgentToken, providedHash))
        {
            // Hash doğrulaması başarılı
        }
        else if (TokenEquals(device.AgentToken, request.AgentToken))
        {
            // Geriye uyumluluk: Eski düz metin token bulundu, derhal hash'e yükselt
            device.AgentToken = providedHash;
        }
        else
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

        if (request.NetworkAdapters != null)
        {
            try
            {
                device.NetworkAdaptersJson = JsonSerializer.Serialize(request.NetworkAdapters);
            }
            catch { }
        }

        if (request.InstalledApps != null)
        {
            try
            {
                device.InstalledAppsJson = JsonSerializer.Serialize(request.InstalledApps);
            }
            catch { }
        }

        if (request.WindowsUpdates != null)
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
    /// Cihazları sunucu tarafında filtreleyerek, sıralayarak ve sayfalayarak döner (Madde 7).
    /// </summary>
    public PagedResult<DeviceSummary> ListPaged(DeviceQueryOptions options)
    {
        using var db = _dbFactory.CreateDbContext();

        var page = Math.Max(1, options.Page);
        var pageSize = Math.Clamp(options.PageSize, 1, 200);

        var query = db.Devices.AsNoTracking().AsQueryable();

        // 1. Grup filtresi
        if (options.GroupId.HasValue)
        {
            query = query.Where(d => d.GroupId == options.GroupId.Value);
        }

        // 2. Arama filtresi
        if (!string.IsNullOrWhiteSpace(options.Search))
        {
            var search = options.Search.Trim().ToLowerInvariant();
            query = query.Where(d =>
                d.DeviceName.ToLower().Contains(search) ||
                d.DomainName.ToLower().Contains(search) ||
                (d.IpAddress != null && d.IpAddress.ToLower().Contains(search)) ||
                (d.ActiveUser != null && d.ActiveUser.ToLower().Contains(search)) ||
                (d.SerialNumber != null && d.SerialNumber.ToLower().Contains(search)) ||
                (d.LocationCode != null && d.LocationCode.ToLower().Contains(search)));
        }

        // SQLite DateTimeOffset kısıtlaması nedeniyle entity'leri belleğe çek
        var allMatching = query.ToList();
        var now = DateTimeOffset.UtcNow;
        var onlineCutoff = now.AddSeconds(-60);

        var onlineCount = allMatching.Count(d => d.LastSeenAt >= onlineCutoff);
        var offlineCount = allMatching.Count - onlineCount;

        // 3. Durum filtresi (Online / Offline)
        if (string.Equals(options.Status, "online", StringComparison.OrdinalIgnoreCase))
        {
            allMatching = allMatching.Where(d => d.LastSeenAt >= onlineCutoff).ToList();
        }
        else if (string.Equals(options.Status, "offline", StringComparison.OrdinalIgnoreCase))
        {
            allMatching = allMatching.Where(d => d.LastSeenAt < onlineCutoff).ToList();
        }

        // 4. Sıralama (In-Memory)
        var isDesc = !string.Equals(options.SortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IEnumerable<DeviceEntity> sorted = (options.SortBy?.ToLowerInvariant()) switch
        {
            "name" => isDesc ? allMatching.OrderByDescending(d => d.DeviceName, StringComparer.OrdinalIgnoreCase) : allMatching.OrderBy(d => d.DeviceName, StringComparer.OrdinalIgnoreCase),
            "cpu" => isDesc ? allMatching.OrderByDescending(d => d.CpuUsagePercent) : allMatching.OrderBy(d => d.CpuUsagePercent),
            "memory" => isDesc ? allMatching.OrderByDescending(d => d.MemoryUsedMb) : allMatching.OrderBy(d => d.MemoryUsedMb),
            "os" => isDesc ? allMatching.OrderByDescending(d => d.OperatingSystem, StringComparer.OrdinalIgnoreCase) : allMatching.OrderBy(d => d.OperatingSystem, StringComparer.OrdinalIgnoreCase),
            "user" => isDesc ? allMatching.OrderByDescending(d => d.ActiveUser, StringComparer.OrdinalIgnoreCase) : allMatching.OrderBy(d => d.ActiveUser, StringComparer.OrdinalIgnoreCase),
            "uptime" => isDesc ? allMatching.OrderByDescending(d => d.UptimeSeconds) : allMatching.OrderBy(d => d.UptimeSeconds),
            _ => isDesc ? allMatching.OrderByDescending(d => d.LastSeenAt) : allMatching.OrderBy(d => d.LastSeenAt)
        };

        var filteredCount = allMatching.Count;
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)pageSize));

        var pagedItems = sorted
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => ToSummary(d))
            .ToList();

        return new PagedResult<DeviceSummary>(
            pagedItems,
            filteredCount,
            page,
            pageSize,
            totalPages,
            onlineCount,
            offlineCount);
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

        // İlişkili kayıtları temizle (Cascade Cleanup)
        var relatedCommands = db.DeviceCommands.Where(c => c.DeviceId == id);
        db.DeviceCommands.RemoveRange(relatedCommands);

        var relatedAlerts = db.DeviceAlerts.Where(a => a.DeviceId == id);
        db.DeviceAlerts.RemoveRange(relatedAlerts);

        var relatedSessions = db.RemoteSessions.Where(s => s.DeviceId == id);
        db.RemoteSessions.RemoveRange(relatedSessions);

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
        var device = db.Devices.FirstOrDefault(d => d.Id == deviceId);
        if (device is null)
        {
            return false;
        }

        var providedHash = SessionTokens.Hash(agentToken);
        if (TokenEquals(device.AgentToken, providedHash))
        {
            return true;
        }

        // Geriye uyumluluk: DB'de eski düz metin token kayıtlıysa
        if (TokenEquals(device.AgentToken, agentToken))
        {
            device.AgentToken = providedHash;
            try
            {
                db.SaveChanges();
            }
            catch { }
            return true;
        }

        return false;
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
            device.UptimeSeconds,
            adapters,
            apps,
            updates,
            device.SerialNumber,
            hardware,
            device.SecurityProfileId,
            device.GroupId);
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

    private static bool IsGenericSerial(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        var t = s.Trim().ToLowerInvariant();
        return t is "0" or "none" or "default string" or "to be filled by o.e.m." or "system serial number" or "chassis serial number";
    }
}
