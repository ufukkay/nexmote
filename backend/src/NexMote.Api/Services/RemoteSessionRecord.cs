namespace NexMote.Api.Services;

public sealed record RemoteSessionRecord(Guid Id, Guid DeviceId, string Token, DateTimeOffset ExpiresAt);

