using System.Collections.Concurrent;

namespace NexMote.Api.Services;

public enum SignalSessionRole
{
    Technician,
    Agent
}

public sealed class SignalSessionAccess
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, SignalSessionRole>> _memberships = new();

    public void Add(string connectionId, Guid sessionId, SignalSessionRole role)
    {
        var sessions = _memberships.GetOrAdd(connectionId, _ => new ConcurrentDictionary<Guid, SignalSessionRole>());
        sessions[sessionId] = role;
    }

    public bool Has(string connectionId, Guid sessionId) =>
        _memberships.TryGetValue(connectionId, out var sessions) && sessions.ContainsKey(sessionId);

    public void RemoveConnection(string connectionId)
    {
        _memberships.TryRemove(connectionId, out _);
    }
}
