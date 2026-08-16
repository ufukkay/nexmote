namespace NexMote.Shared.Contracts;

/// <summary>
/// Teknisyen tarafından belirli bir cihaza canlı bağlantı oturumu başlatma isteği.
/// </summary>
/// <param name="DeviceId">Bağlanılmak istenen hedef cihazın benzersiz kimliği.</param>
public sealed record CreateRemoteSessionRequest(Guid DeviceId);
