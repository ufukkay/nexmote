using System.Text.Json;

namespace NexMote.Agent.Windows;

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

    public async Task<DeviceIdentity?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_identityPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(_identityPath);
        return await JsonSerializer.DeserializeAsync<DeviceIdentity>(stream, cancellationToken: cancellationToken);
    }

    public async Task SaveAsync(DeviceIdentity identity, CancellationToken cancellationToken)
    {
        await using var stream = File.Create(_identityPath);
        await JsonSerializer.SerializeAsync(stream, identity, cancellationToken: cancellationToken);
    }

    public void Delete()
    {
        File.Delete(_identityPath);
    }
}
