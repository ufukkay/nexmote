namespace NexMote.Shared.Contracts;

public sealed record AgentEnrollmentRequest(
    string EnrollmentKey,
    string DeviceName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    string? SerialNumber,
    string? LocationCode);

