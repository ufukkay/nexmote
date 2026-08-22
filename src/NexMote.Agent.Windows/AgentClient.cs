using Microsoft.Extensions.Options;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;
using NexMote.Shared.Telemetry;
using System.Net.Http.Json;
using System.Reflection;

namespace NexMote.Agent.Windows;

/// <summary>
/// Windows Agent servisinin NexMote sunucusu ile REST API üzerinden iletişim kurmasını
/// (Enrollment ve Heartbeat) sağlayan istemci.
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
            SystemTelemetry.GetFriendlyOperatingSystemName(),
            RunningVersion,
            null,
            options.LocationCode ?? "OFFICE");

        var cleanServerUrl = GetCleanServerUrl(options.ServerUrl);
        var url = BuildUrl(cleanServerUrl, "api/agents/enroll");
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

        // Aktif konsol oturumunda giriş yapmış veya son oturum açan kullanıcının temiz adını al (domain öneki olmadan)
        var activeUser = SessionProcessLauncher.GetActiveSessionUserName();

        var hardware = SystemTelemetry.GetHardwareInventory();

        var request = new DeviceHeartbeatRequest(
            identity.AgentToken,
            activeUser,
            SystemTelemetry.GetPrimaryIPv4Address(),
            _cpuUsageSampler.GetAveragePercent(),
            totalRamMb,
            usedRamMb,
            SystemTelemetry.GetDiskFreeMb(),
            Environment.TickCount64 / 1000,
            RunningVersion,
            SystemTelemetry.GetNetworkAdapters(),
            SystemTelemetry.GetInstalledApplications(),
            SystemTelemetry.GetInstalledWindowsUpdates(),
            hardware.SystemSerialNumber,
            hardware);

        var cleanServerUrl = GetCleanServerUrl(options.ServerUrl);
        var url = BuildUrl(cleanServerUrl, $"api/agents/{identity.DeviceId}/heartbeat");
        var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// SYSTEM yetkisiyle (Worker'ın doğrudan SignalR Hub bağlantısı üzerinden) çalıştırılan uzak komutların
    /// denetim kaydını sunucuya iletir. Bu olmadan en yetkili komut çalıştırma yolu hiç audit izi bırakmaz.
    /// </summary>
    public async Task PostCommandAuditAsync(DeviceIdentity identity, Guid sessionId, string shell, string command, int exitCode, string stdOut, string stdErr, long durationMs, CancellationToken cancellationToken)
    {
        try
        {
            var options = _optionsMonitor.CurrentValue;
            var entry = new CommandAuditEntry(
                identity.DeviceId,
                identity.AgentToken,
                sessionId,
                shell,
                command,
                exitCode,
                Truncate(stdOut, 2000),
                Truncate(stdErr, 2000),
                durationMs,
                DateTimeOffset.UtcNow);

            var cleanServerUrl = GetCleanServerUrl(options.ServerUrl);
            var url = BuildUrl(cleanServerUrl, "api/audit/commands");
            await _httpClient.PostAsJsonAsync(url, entry, cancellationToken);
        }
        catch
        {
            // Audit iletimi best-effort'tur; komut zaten çalıştı ve sonucu teknisyene döndürüldü.
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length > max ? value[..max] : value;
    }

    /// <summary>
    /// Sunucu URL'ini doğrular ve güvenli hale getirir (yerel/özel adresleri üretim sunucusuna zorlar).
    /// Tray sürecinin de aynı kuralı uygulaması gerektiğinden, gerçek mantık paylaşımlı
    /// <see cref="NexMoteHttp.EnforceProductionUrl"/> içinde tutulur — burada tekrarlanmaz.
    /// </summary>
    internal static string GetCleanServerUrl(string? rawUrl) => NexMoteHttp.EnforceProductionUrl(rawUrl);

    /// <summary>
    /// Base URL ve path'i güvenli şekilde birleştirir.
    /// </summary>
    private static Uri BuildUrl(string baseUrl, string path)
    {
        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), path);
    }
}
