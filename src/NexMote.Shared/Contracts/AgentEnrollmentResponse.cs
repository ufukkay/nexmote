namespace NexMote.Shared.Contracts;

/// <summary>
/// Sunucu tarafından cihaz kaydı onaylandığında Agent'a iletilen yanıt.
/// </summary>
/// <param name="DeviceId">Veritabanında cihaza atanan benzersiz Guid kimlik.</param>
/// <param name="AgentToken">Cihazın sonraki tüm heartbeat ve audit işlemlerinde kullanacağı 32-byte güvenlik token'ı.</param>
/// <param name="SignalingHubPath">SignalR WebSocket canlı yayın hub bağlantı yolu (/hubs/signaling).</param>
/// <param name="HeartbeatInterval">Sunucunun talep ettiği heartbeat gönderme aralığı.</param>
public sealed record AgentEnrollmentResponse(
    Guid DeviceId,
    string AgentToken,
    Uri SignalingHubPath,
    TimeSpan HeartbeatInterval);
