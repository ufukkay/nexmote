namespace NexMote.Api.Services;

/// <summary>
/// Bellek içi ve servis katmanında kullanılan cihaz kayıt modeli.
/// </summary>
public sealed class DeviceRecord
{
    public DeviceRecord(Guid id, string deviceName, string domainName)
    {
        Id = id;
        DeviceName = deviceName;
        DomainName = domainName;
    }

    /// <summary>Cihaz benzersiz kimliği.</summary>
    public Guid Id { get; }

    /// <summary>Bilgisayar adı.</summary>
    public string DeviceName { get; }

    /// <summary>Çalışma grubu veya domain adı.</summary>
    public string DomainName { get; }

    /// <summary>İşletim sistemi bilgisi.</summary>
    public string OperatingSystem { get; set; } = "Unknown";

    /// <summary>Yüklü Agent sürümü.</summary>
    public string AgentVersion { get; set; } = "0.0.0";

    /// <summary>Seri numarası.</summary>
    public string? SerialNumber { get; set; }

    /// <summary>Lokasyon kodu.</summary>
    public string? LocationCode { get; set; }

    /// <summary>Aktif kullanıcı hesabı.</summary>
    public string? ActiveUser { get; set; }

    /// <summary>Yerel IP adresi.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Cihaz güvenlik token'ı.</summary>
    public string AgentToken { get; set; } = string.Empty;

    /// <summary>CPU kullanım yüzdesi.</summary>
    public int CpuUsagePercent { get; set; }

    /// <summary>Toplam fiziksel bellek (MB).</summary>
    public long MemoryTotalMb { get; set; }

    /// <summary>Kullanılan bellek (MB).</summary>
    public long MemoryUsedMb { get; set; }

    /// <summary>Boş disk alanı (MB).</summary>
    public long DiskFreeMb { get; set; }

    /// <summary>Açık kalma süresi (Saniye).</summary>
    public long UptimeSeconds { get; set; }

    /// <summary>Son heartbeat zamanı.</summary>
    public DateTimeOffset LastSeenAt { get; set; }
}
