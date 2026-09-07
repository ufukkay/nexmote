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
    private readonly ConcurrentDictionary<string, Guid> _deviceConnections = new();
    private readonly ConcurrentDictionary<string, Guid> _serviceConnections = new();

    public void Add(string connectionId, Guid sessionId, SignalSessionRole role)
    {
        var sessions = _memberships.GetOrAdd(connectionId, _ => new ConcurrentDictionary<Guid, SignalSessionRole>());
        sessions[sessionId] = role;
    }

    public bool Has(string connectionId, Guid sessionId) =>
        _memberships.TryGetValue(connectionId, out var sessions) && sessions.ContainsKey(sessionId);

    public SignalSessionRole? GetRole(string connectionId, Guid sessionId) =>
        _memberships.TryGetValue(connectionId, out var sessions) && sessions.TryGetValue(sessionId, out var role)
            ? role
            : null;

    public void AddDeviceConnection(string connectionId, Guid deviceId)
    {
        _deviceConnections[connectionId] = deviceId;
    }

    public void AddServiceConnection(string connectionId, Guid deviceId)
    {
        _serviceConnections[connectionId] = deviceId;
    }

    public bool HasServiceConnection(Guid deviceId) =>
        _serviceConnections.Values.Contains(deviceId);

    public bool IsDeviceConnection(string connectionId, Guid deviceId) =>
        _deviceConnections.TryGetValue(connectionId, out var registeredDeviceId) && registeredDeviceId == deviceId;

    public void RemoveConnection(string connectionId)
    {
        _memberships.TryRemove(connectionId, out _);
        _deviceConnections.TryRemove(connectionId, out _);
        _serviceConnections.TryRemove(connectionId, out _);
    }
}
