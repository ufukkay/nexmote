using System.Collections.Concurrent;
using System.Security.Cryptography;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

public sealed class DeviceRegistry
{
    private readonly ConcurrentDictionary<Guid, DeviceRecord> _devices = new();

    public AgentEnrollmentResponse Enroll(AgentEnrollmentRequest request)
    {
        var existing = _devices.Values.FirstOrDefault(device =>
            string.Equals(device.DeviceName, request.DeviceName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(device.DomainName, request.DomainName, StringComparison.OrdinalIgnoreCase));

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var record = existing is null
            ? new DeviceRecord(Guid.NewGuid(), request.DeviceName, request.DomainName)
            : existing;

        record.OperatingSystem = request.OperatingSystem;
        record.AgentVersion = request.AgentVersion;
        record.SerialNumber = request.SerialNumber;
        record.LocationCode = request.LocationCode;
        record.AgentToken = token;
        record.LastSeenAt = DateTimeOffset.UtcNow;

        _devices[record.Id] = record;

        return new AgentEnrollmentResponse(
            record.Id,
            token,
            new Uri("/hubs/signaling", UriKind.Relative),
            TimeSpan.FromSeconds(20));
    }

    public bool Heartbeat(Guid deviceId, DeviceHeartbeatRequest request)
    {
        if (!_devices.TryGetValue(deviceId, out var device) || device.AgentToken != request.AgentToken)
        {
            return false;
        }

        device.ActiveUser = request.ActiveUser;
        device.IpAddress = request.IpAddress;
        device.CpuUsagePercent = request.CpuUsagePercent;
        device.MemoryTotalMb = request.MemoryTotalMb;
        device.MemoryUsedMb = request.MemoryUsedMb;
        device.DiskFreeMb = request.DiskFreeMb;
        device.UptimeSeconds = request.UptimeSeconds;
        device.LastSeenAt = DateTimeOffset.UtcNow;
        return true;
    }

    public IReadOnlyCollection<DeviceSummary> List()
    {
        return _devices.Values
            .OrderByDescending(device => device.LastSeenAt)
            .Select(ToSummary)
            .ToArray();
    }

    public DeviceSummary? Get(Guid deviceId)
    {
        return _devices.TryGetValue(deviceId, out var device) ? ToSummary(device) : null;
    }

    public bool ValidateAgent(Guid deviceId, string agentToken)
    {
        return _devices.TryGetValue(deviceId, out var device) &&
            string.Equals(device.AgentToken, agentToken, StringComparison.Ordinal);
    }

    private static DeviceSummary ToSummary(DeviceRecord device)
    {
        return new DeviceSummary(
            device.Id,
            device.DeviceName,
            device.DomainName,
            device.OperatingSystem,
            device.AgentVersion,
            device.ActiveUser,
            device.IpAddress,
            device.LocationCode,
            DateTimeOffset.UtcNow - device.LastSeenAt < TimeSpan.FromMinutes(2),
            device.LastSeenAt);
    }
}
