using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;

namespace NexMote.Agent.Windows.Security;

/// <summary>
/// Ajan tarafında politika senkronizasyonu, disk önbelleklemesi (offline resilience)
/// ve güvenlik kurallarının (USB, koruma) uygulanmasını koordine eden merkezi yönetici.
/// </summary>
public sealed class PolicyManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _cachePath;
    private readonly ILogger<PolicyManager> _logger;
    private PolicyDocument? _activePolicy;
    private readonly object _lock = new();

    public PolicyManager(ILogger<PolicyManager> logger)
    {
        _logger = logger;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
        Directory.CreateDirectory(dir);
        _cachePath = Path.Combine(dir, "policy-cache.json");
    }

    public PolicyDocument ActivePolicy
    {
        get
        {
            lock (_lock)
            {
                return _activePolicy ?? LoadFromDisk() ?? PolicyDocument.CreateDefaultRoot();
            }
        }
    }

    /// <summary>
    /// Servis ilk başladığında ağ bağlantısını beklemeden diskteki son geçerli politikayı anında uygular.
    /// </summary>
    public void EnforceStartupPolicy()
    {
        try
        {
            var cached = LoadFromDisk();
            if (cached != null)
            {
                lock (_lock)
                {
                    _activePolicy = cached;
                }
                _logger.LogInformation("Önbellekten politika yüklendi (v{Version}). Güvenlik kuralları uygulanıyor...", cached.Version);
                UsbPolicyEnforcer.Enforce(cached.Usb, _logger);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Açılış politikası önbellekten yüklenirken hata oluştu.");
        }
    }

    /// <summary>
    /// Sunucudan en güncel politikayı sorgular. Değişiklik varsa USB kurallarını uygular,
    /// diske önbelleğe yazar ve sunucuya ACK gönderir.
    /// </summary>
    public async Task SyncAndEnforcePolicyAsync(string serverUrl, DeviceIdentity identity, CancellationToken cancellationToken)
    {
        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(15));
            var url = $"{serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/policy?agentToken={Uri.EscapeDataString(identity.AgentToken)}";
            var response = await http.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Politika sorgusu başarısız (HTTP {StatusCode}). Mevcut politika korunuyor.", response.StatusCode);
                return;
            }

            var remotePolicy = await response.Content.ReadFromJsonAsync<PolicyDocument>(JsonOptions, cancellationToken);
            if (remotePolicy == null) return;

            var current = ActivePolicy;
            var isNewVersion = remotePolicy.Version != current.Version || _activePolicy == null;

            if (isNewVersion)
            {
                _logger.LogInformation("Yeni politika sürümü algılandı: v{RemoteVersion} (Önceki: v{LocalVersion}). Uygulanıyor...",
                    remotePolicy.Version, current.Version);

                lock (_lock)
                {
                    _activePolicy = remotePolicy;
                }

                // 1. USB Güvenlik Politikasını zorla
                UsbPolicyEnforcer.Enforce(remotePolicy.Usb, _logger);

                // 2. Diske güvenle kaydet
                SaveToDisk(remotePolicy);

                // 3. Sunucuya başarılı uygulandığını teyit et (ACK)
                await SendPolicyAckAsync(http, serverUrl, identity, remotePolicy.Version, cancellationToken);

                // 4. Masaüstü tepsisine (Tray) ve oturuma bildir (Dosya / Sinyal üzerinden)
                NotifyTraySession(remotePolicy);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Politika senkronizasyonu sırasında hata oluştu. Yerel önbellek korunuyor.");
        }
    }

    private static async Task SendPolicyAckAsync(HttpClient http, string serverUrl, DeviceIdentity identity, int version, CancellationToken cancellationToken)
    {
        try
        {
            var ackUrl = $"{serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/policy-ack?agentToken={Uri.EscapeDataString(identity.AgentToken)}&appliedVersion={version}";
            await http.PostAsync(ackUrl, null, cancellationToken);
        }
        catch
        {
        }
    }

    private void NotifyTraySession(PolicyDocument policy)
    {
        try
        {
            // Tray süreci de aynı ProgramData altındaki güncel policy-cache.json dosyasını okuyup
            // branding ve consent ayarlarını anında yeniler.
            var notifyFilePath = Path.Combine(Path.GetDirectoryName(_cachePath)!, "policy-updated.signal");
            File.WriteAllText(notifyFilePath, policy.Version.ToString());
        }
        catch
        {
        }
    }

    private PolicyDocument? LoadFromDisk()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                var json = File.ReadAllText(_cachePath);
                return JsonSerializer.Deserialize<PolicyDocument>(json, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Politika önbellek dosyası okunamadı: {Path}", _cachePath);
        }
        return null;
    }

    private void SaveToDisk(PolicyDocument policy)
    {
        try
        {
            var json = JsonSerializer.Serialize(policy, JsonOptions);
            File.WriteAllText(_cachePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Politika önbellek dosyası yazılamadı: {Path}", _cachePath);
        }
    }
}
