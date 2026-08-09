using Microsoft.Extensions.Options;
using NexMote.Shared.Contracts;
using System.Net.Http.Json;

namespace NexMote.Agent.Windows;

public sealed class AgentClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AgentOptions> _optionsMonitor;

    public AgentClient(HttpClient httpClient, IOptionsMonitor<AgentOptions> optionsMonitor)
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
    }

    public async Task<DeviceIdentity> EnrollAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var request = new AgentEnrollmentRequest(
            options.EnrollmentKey,
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.OSVersion.VersionString,
            options.AgentVersion,
            null,
            options.LocationCode);

        var url = new Uri(new Uri(options.ServerUrl.TrimEnd('/') + "/"), "api/agents/enroll");
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AgentEnrollmentResponse>(cancellationToken);
        if (body is null)
        {
            throw new InvalidOperationException("Enrollment response could not be parsed.");
        }

        return new DeviceIdentity(body.DeviceId, body.AgentToken);
    }

    public async Task SendHeartbeatAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var request = new DeviceHeartbeatRequest(
            identity.AgentToken,
            $"{Environment.UserDomainName}\\{Environment.UserName}",
            NetworkInfo.GetPrimaryIPv4Address(),
            0,
            0,
            0,
            0,
            Environment.TickCount64 / 1000);

        var url = new Uri(new Uri(options.ServerUrl.TrimEnd('/') + "/"), $"api/agents/{identity.DeviceId}/heartbeat");
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
