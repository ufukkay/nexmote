using Microsoft.AspNetCore.SignalR;
using NexMote.Api.Services;

namespace NexMote.Api.Hubs;

public sealed class SignalingHub : Hub
{
    private readonly RemoteSessionRegistry _sessions;
    private readonly DeviceRegistry _devices;

    public SignalingHub(RemoteSessionRegistry sessions, DeviceRegistry devices)
    {
        _sessions = sessions;
        _devices = devices;
    }

    public async Task JoinTechnicianSession(Guid sessionId, string token)
    {
        var session = _sessions.Get(sessionId);
        if (session is null || session.Token != token)
        {
            throw new HubException("Invalid or expired session.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        await Clients.Group($"device:{session.DeviceId}").SendAsync("RemoteSessionRequested", sessionId);
    }

    public async Task JoinDevice(Guid deviceId, string agentToken)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            throw new HubException("Invalid device token.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
    }

    public async Task JoinDeviceSession(Guid sessionId, Guid deviceId, string agentToken)
    {
        var session = _sessions.Get(sessionId);
        if (session is null || session.DeviceId != deviceId || !_devices.ValidateAgent(deviceId, agentToken))
        {
            throw new HubException("Invalid or expired device session.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        await Clients.Group($"session:{sessionId}").SendAsync("DeviceJoinedSession");
    }

    public async Task SendSignal(Guid sessionId, string type, string payload)
    {
        if (_sessions.Get(sessionId) is null)
        {
            throw new HubException("Invalid or expired session.");
        }

        if (string.IsNullOrWhiteSpace(type) || payload is null)
        {
            throw new HubException("Signal payload is required.");
        }

        if (string.Equals(type, "remote-input", StringComparison.OrdinalIgnoreCase) && payload.Length > 4096)
        {
            throw new HubException("Remote input payload is too large.");
        }

        await Clients.OthersInGroup($"session:{sessionId}").SendAsync("SignalReceived", type, payload);
    }
}
