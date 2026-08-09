using Microsoft.Extensions.Options;
using NexMote.Shared.Contracts;
using System.Net.Http.Json;

namespace NexMote.Agent.Windows;

public sealed class AgentClient
{
    private readonly HttpClient _httpClient;
    private readonly AgentOptions _options;

    public AgentClient(HttpClient httpClient, IOptions<AgentOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.ServerUrl);
    }

    public async Task<DeviceIdentity> EnrollAsync(CancellationToken cancellationToken)
    {
        var request = new AgentEnrollmentRequest(
            _options.EnrollmentKey,
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.OSVersion.VersionString,
            _options.AgentVersion,
            null,
            _options.LocationCode);

        var response = await _httpClient.PostAsJsonAsync("/api/agents/enroll", request, cancellationToken);
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
        var request = new DeviceHeartbeatRequest(
            identity.AgentToken,
            $"{Environment.UserDomainName}\\{Environment.UserName}",
            NetworkInfo.GetPrimaryIPv4Address(),
            0,
            0,
            0,
            0,
            Environment.TickCount64 / 1000);

        var response = await _httpClient.PostAsJsonAsync($"/api/agents/{identity.DeviceId}/heartbeat", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
