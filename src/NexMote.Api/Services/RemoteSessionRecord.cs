namespace NexMote.Api.Services;

/// <summary>
/// Bellek içi ve oturum sorgularında kullanılan aktif oturum kaydı.
/// </summary>
/// <param name="Id">Oturum kimliği.</param>
/// <param name="DeviceId">Hedef cihaz kimliği.</param>
/// <param name="Token">Oturum güvenlik doğrulama token'ı.</param>
/// <param name="ExpiresAt">Geçerlilik bitiş zamanı.</param>
public sealed record RemoteSessionRecord(Guid Id, Guid DeviceId, string Token, DateTimeOffset ExpiresAt);
