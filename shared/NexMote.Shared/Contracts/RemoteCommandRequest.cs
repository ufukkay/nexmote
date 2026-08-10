namespace NexMote.Shared.Contracts;

public sealed record RemoteCommandRequest(
    Guid SessionId,
    string RequestId,
    string Shell,
    string Command);
