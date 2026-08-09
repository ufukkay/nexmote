using System.Collections.Concurrent;
using System.Security.Cryptography;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

public sealed class RemoteSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, RemoteSessionRecord> _sessions = new();

    public CreateRemoteSessionResponse Create(Guid deviceId, string serverUrl)
    {
        var id = Guid.NewGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(5);

        _sessions[id] = new RemoteSessionRecord(id, deviceId, token, expiresAt);

        var launchUri = $"nexmote://connect?sessionId={id}&token={Uri.EscapeDataString(token)}&serverUrl={Uri.EscapeDataString(serverUrl.TrimEnd('/'))}";
        return new CreateRemoteSessionResponse(id, deviceId, launchUri, expiresAt);
    }

    public RemoteSessionRecord? Get(Guid sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) && session.ExpiresAt > DateTimeOffset.UtcNow
            ? session
            : null;
    }
}
