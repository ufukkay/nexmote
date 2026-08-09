namespace NexMote.Api.Services;

public sealed class DeviceRecord
{
    public DeviceRecord(Guid id, string deviceName, string domainName)
    {
        Id = id;
        DeviceName = deviceName;
        DomainName = domainName;
    }

    public Guid Id { get; }
    public string DeviceName { get; }
    public string DomainName { get; }
    public string OperatingSystem { get; set; } = "Unknown";
    public string AgentVersion { get; set; } = "0.0.0";
    public string? SerialNumber { get; set; }
    public string? LocationCode { get; set; }
    public string? ActiveUser { get; set; }
    public string? IpAddress { get; set; }
    public string AgentToken { get; set; } = string.Empty;
    public int CpuUsagePercent { get; set; }
    public long MemoryTotalMb { get; set; }
    public long MemoryUsedMb { get; set; }
    public long DiskFreeMb { get; set; }
    public long UptimeSeconds { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
}

