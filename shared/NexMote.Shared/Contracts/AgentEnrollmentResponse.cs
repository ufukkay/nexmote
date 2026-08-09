namespace NexMote.Shared.Contracts;

public sealed record AgentEnrollmentResponse(
    Guid DeviceId,
    string AgentToken,
    Uri SignalingUrl,
    TimeSpan HeartbeatInterval);

