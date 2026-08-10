namespace NexMote.Shared.Contracts;

public sealed record ServerSettingsContract(
    string ServerUrl,
    string EnrollmentKey,
    int HeartbeatSeconds,
    string DefaultLocationCode,
    string TechnicianKey = "");
