using System.Text.Json;
using NexMote.Shared.Network;

namespace NexMote.Agent.Tray;

/// <summary>
/// appsettings.json dosyasından sunucu URL'i ve kayıt anahtarını okuyan veya güncelleyen statik ayar yöneticisi.
/// </summary>
internal static class AgentSettings
{
    public static string LoadServerUrl()
    {
        var raw = LoadSetting("ServerUrl", "https://nexmote.com");
        return NexMoteHttp.EnforceProductionUrl(raw);
    }

    public static string LoadEnrollmentKey()
    {
        var key = LoadSetting("EnrollmentKey", "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7");
        return string.IsNullOrWhiteSpace(key) || key == "dev-enrollment-key" || key.StartsWith("CHANGE-ME")
            ? "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7"
            : key;
    }

    private static string LoadSetting(string propertyName, string defaultValue)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var serviceConfigPath = Path.Combine(programData, "NexMote", "Agent", "appsettings.json");
        var baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        string? result = ReadPropertyFromFile(serviceConfigPath, propertyName);
        if (!string.IsNullOrEmpty(result)) return result;

        result = ReadPropertyFromFile(baseConfigPath, propertyName);
        return string.IsNullOrEmpty(result) ? defaultValue : result;
    }

    private static string? ReadPropertyFromFile(string path, string propertyName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Agent", out var agent) &&
                agent.TryGetProperty(propertyName, out var prop))
            {
                return prop.GetString();
            }
        }
        catch { }
        return null;
    }

    public static void SaveSettings(string newUrl, string newKey)
    {
        var normalizedUrl = NexMoteHttp.NormalizeUrl(newUrl);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var agentDir = Path.Combine(programData, "NexMote", "Agent");
        Directory.CreateDirectory(agentDir);

        var serviceConfigPath = Path.Combine(agentDir, "appsettings.json");
        SaveConfigToPath(serviceConfigPath, normalizedUrl, newKey);

        var baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        SaveConfigToPath(baseConfigPath, normalizedUrl, newKey);
    }

    private static void SaveConfigToPath(string path, string newUrl, string newKey)
    {
        try
        {
            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            var rootObj = string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("{") ? new Dictionary<string, object>() : JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();

            var agentDict = new Dictionary<string, object>
            {
                ["ServerUrl"] = newUrl,
                ["EnrollmentKey"] = newKey,
                ["AgentVersion"] = "0.1.0",
                ["LocationCode"] = "OFFICE",
                ["HeartbeatSeconds"] = 20
            };

            rootObj["Agent"] = agentDict;

            if (!rootObj.ContainsKey("Logging"))
            {
                rootObj["Logging"] = new Dictionary<string, object>
                {
                    ["LogLevel"] = new Dictionary<string, string>
                    {
                        ["Default"] = "Information",
                        ["Microsoft.Hosting.Lifetime"] = "Information"
                    }
                };
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(rootObj, options);
            File.WriteAllText(path, updatedJson);
        }
        catch
        {
            // Ignore write errors if permissions restricted
        }
    }
}
