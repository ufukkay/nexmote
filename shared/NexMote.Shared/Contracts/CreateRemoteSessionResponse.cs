namespace NexMote.Shared.Contracts;

public sealed record CreateRemoteSessionResponse(
    Guid SessionId,
    Guid DeviceId,
    string LaunchUri,
    DateTimeOffset ExpiresAt);

