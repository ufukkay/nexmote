using Microsoft.Extensions.Options;
using NexMote.Shared.Contracts;
using System.Net.Http.Json;
using System.Reflection;

namespace NexMote.Agent.Windows;

/// <summary>
/// Windows Agent servisinin NexMote sunucusu ile REST API üzerinden iletişim kurmasını (Enrollment ve Heartbeat) sağlayan istemci.
/// </summary>
public sealed class AgentClient
{
    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<AgentOptions> _optionsMonitor;
    private readonly CpuUsageSampler _cpuUsageSampler;

    private static readonly string RunningVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public AgentClient(HttpClient httpClient, IOptionsMonitor<AgentOptions> optionsMonitor, CpuUsageSampler cpuUsageSampler)
    {
        _httpClient = httpClient;
        _optionsMonitor = optionsMonitor;
        _cpuUsageSampler = cpuUsageSampler;
    }

    /// <summary>
    /// Cihazın ilk sunucu kaydını yapar (/api/agents/enroll) ve atanan kimlik ile güvenlik token'ını döner.
    /// </summary>
    public async Task<DeviceIdentity> EnrollAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var request = new AgentEnrollmentRequest(
            options.EnrollmentKey,
            Environment.MachineName,
            Environment.UserDomainName,
            Environment.OSVersion.VersionString,
            RunningVersion,
            null,
            options.LocationCode ?? "OFFICE");

        var cleanServerUrl = GetCleanServerUrl(options.ServerUrl);
        var url = new Uri(new Uri(cleanServerUrl + "/"), "api/agents/enroll");
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AgentEnrollmentResponse>(cancellationToken);
        if (body is null)
        {
            throw new InvalidOperationException("Kayıt (Enrollment) yanıtı ayrıştırılamadı.");
        }

        return new DeviceIdentity(body.DeviceId, body.AgentToken);
    }

    /// <summary>
    /// Sunucuya periyodik canlılık sinyali ve donanım metriklerini (/api/agents/{id}/heartbeat) iletir.
    /// </summary>
    public async Task SendHeartbeatAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        var (totalRamMb, usedRamMb) = SystemTelemetry.GetMemoryMetrics();
        var request = new DeviceHeartbeatRequest(
            identity.AgentToken,
            $"{Environment.UserDomainName}\\{Environment.UserName}",
            SystemTelemetry.GetPrimaryIPv4Address(),
            _cpuUsageSampler.GetAveragePercent(),
            totalRamMb,
            usedRamMb,
            SystemTelemetry.GetDiskFreeMb(),
            Environment.TickCount64 / 1000,
            RunningVersion);

        var cleanServerUrl = GetCleanServerUrl(options.ServerUrl);
        var url = new Uri(new Uri(cleanServerUrl + "/"), $"api/agents/{identity.DeviceId}/heartbeat");
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Sunucu URL'indeki olası geçersiz yerel IP veya eksiklikleri temizleyerek canlı adrese zorlar.
    /// </summary>
    private static string GetCleanServerUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Contains("192.168.0") || rawUrl.Contains("127.0.0.1") || rawUrl.Contains("localhost") || rawUrl.StartsWith("http://"))
        {
            return "https://nexmote.com";
        }
        return rawUrl.TrimEnd('/');
    }
}
