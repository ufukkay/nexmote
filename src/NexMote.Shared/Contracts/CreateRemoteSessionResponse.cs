namespace NexMote.Shared.Contracts;

/// <summary>
/// Başlatılan uzaktan kontrol oturumunun deep-link ve yetkilendirme detayları.
/// </summary>
/// <param name="SessionId">Oturum için üretilen benzersiz Guid kimlik.</param>
/// <param name="DeviceId">Bağlanılan hedef cihazın kimliği.</param>
/// <param name="LaunchUri">Teknisyen masaüstü uygulamasını tetikleyen "nexmote://connect?..." deep-link URI'ı.</param>
/// <param name="ExpiresAt">Oturum token'ının geçerlilik son kullanma tarihi (5 dakika).</param>
public sealed record CreateRemoteSessionResponse(
    Guid SessionId,
    Guid DeviceId,
    string LaunchUri,
    DateTimeOffset ExpiresAt);
