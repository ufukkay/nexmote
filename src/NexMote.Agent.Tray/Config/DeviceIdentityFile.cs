using System.Net.Http.Json;
using System.Text.Json;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;
using NexMote.Shared.Telemetry;

namespace NexMote.Agent.Tray;

/// <summary>
/// Tray'in kimlik depolama cephesi. Gerçek okuma/yazma, Windows Servisi'yle (NexMote.Agent.Windows) AYNI
/// DPAPI-şifreli identity.dat dosyasını kullanan <see cref="NexMote.Shared.Identity.DeviceIdentityStore"/>
/// üzerinden yapılır.
/// </summary>
internal static class DeviceIdentityFile
{
    private static readonly DeviceIdentityStore Store = new();

    public static DeviceIdentity? Load()
    {
        try
        {
            return Store.Load();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DeviceIdentity?> EnsureEnrolledAsync(string serverUrl, string enrollmentKey)
    {
        var existing = Load();
        if (existing is not null) return existing;

        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
            var enrollUrl = $"{serverUrl.TrimEnd('/')}/api/agents/enroll";

            var os = Environment.OSVersion.VersionString;
            var deviceName = Environment.MachineName;
            var domainName = Environment.UserDomainName;
            var activeUser = SessionUserResolver.GetActiveSessionUserName();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";

            var req = new
            {
                DeviceName = deviceName,
                DomainName = domainName,
                OperatingSystem = os,
                AgentVersion = version,
                ActiveUser = activeUser,
                EnrollmentKey = string.IsNullOrWhiteSpace(enrollmentKey) || enrollmentKey == "dev-enrollment-key"
                    ? "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7"
                    : enrollmentKey,
                LocationCode = "OFFICE"
            };

            var res = await http.PostAsJsonAsync(enrollUrl, req);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
                if (doc != null &&
                    doc.RootElement.TryGetProperty("deviceId", out var idProp) &&
                    doc.RootElement.TryGetProperty("agentToken", out var tokenProp))
                {
                    var id = idProp.GetGuid();
                    var token = tokenProp.GetString() ?? string.Empty;
                    var identity = new DeviceIdentity(id, token);
                    Save(identity);
                    return identity;
                }
            }
        }
        catch { }

        return null;
    }

    public static void Save(DeviceIdentity identity)
    {
        try
        {
            Store.Save(identity);
        }
        catch { }
    }

    public static void Delete()
    {
        try
        {
            Store.Delete();
        }
        catch { }
    }
}
