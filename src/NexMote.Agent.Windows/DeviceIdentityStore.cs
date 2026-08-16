using System.Text.Json;

namespace NexMote.Agent.Windows;

/// <summary>
/// Cihaza ait kimlik bilgilerini (DeviceId ve AgentToken) %ProgramData%\NexMote\Agent\identity.json
/// dosyasında güvenli ve kalıcı olarak saklayan, okuyan veya sıfırlayan depo sınıfı.
/// </summary>
public sealed class DeviceIdentityStore
{
    private readonly string _identityPath;

    public DeviceIdentityStore()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var directory = Path.Combine(programData, "NexMote", "Agent");
        Directory.CreateDirectory(directory);
        _identityPath = Path.Combine(directory, "identity.json");
    }

    /// <summary>
    /// Yerel diskteki identity.json dosyasından cihaz kimliğini okur. Dosya yoksa null döner.
    /// </summary>
    public async Task<DeviceIdentity?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_identityPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_identityPath);
        return await JsonSerializer.DeserializeAsync<DeviceIdentity>(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Yeni kayıt edilen cihaz kimliğini identity.json dosyasına asenkron olarak yazar.
    /// </summary>
    public async Task SaveAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_identityPath);
        await JsonSerializer.SerializeAsync(stream, identity, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Kimlik dosyasını siler (Sunucu adresi değiştiğinde veya yeniden kayıt gerektiğinde çağrılır).
    /// </summary>
    public void Delete()
    {
        File.Delete(_identityPath);
    }
}
