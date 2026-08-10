namespace NexMote.Shared.Contracts;

public sealed record RemoteCommandResult(
    Guid SessionId,
    string RequestId,
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    bool TimedOut);
