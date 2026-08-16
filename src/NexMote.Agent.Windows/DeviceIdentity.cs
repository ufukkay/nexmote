namespace NexMote.Agent.Windows;

/// <summary>
/// Cihazın sunucu kaydı sonrasında yerel diskte saklanan kimlik ve güvenlik token'ı.
/// </summary>
/// <param name="DeviceId">Veritabanındaki benzersiz cihaz kimliği.</param>
/// <param name="AgentToken">Heartbeat ve yetkilendirmelerde kullanılan 32-byte güvenlik token'ı.</param>
public sealed record DeviceIdentity(Guid DeviceId, string AgentToken);
