namespace NexMote.Shared.Contracts;

public sealed record DeviceHeartbeatRequest(
    string AgentToken,
    string? ActiveUser,
    string? IpAddress,
    int CpuUsagePercent,
    long MemoryTotalMb,
    long MemoryUsedMb,
    long DiskFreeMb,
    long UptimeSeconds);

