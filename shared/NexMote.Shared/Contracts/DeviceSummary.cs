namespace NexMote.Shared.Contracts;

public sealed record DeviceSummary(
    Guid Id,
    string DeviceName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    string? ActiveUser,
    string? IpAddress,
    string? LocationCode,
    bool IsOnline,
    DateTimeOffset LastSeenAt);

