namespace NexMote.Shared.Contracts;

public sealed record CommandAuditEntry(
    Guid DeviceId,
    string AgentToken,
    Guid SessionId,
    string Shell,
    string Command,
    int ExitCode,
    string StdOutPreview,
    string StdErrPreview,
    long DurationMs,
    DateTimeOffset ExecutedAt);
