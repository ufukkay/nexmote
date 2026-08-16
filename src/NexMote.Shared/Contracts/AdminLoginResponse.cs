namespace NexMote.Shared.Contracts;

/// <summary>
/// Başarılı admin girişi sonrasında sunucunun döndürdüğü kimlik doğrulama yanıtı.
/// </summary>
/// <param name="Token">Korumalı admin API isteklerinde "Authorization: Bearer [Token]" olarak kullanılacak anahtar.</param>
public sealed record AdminLoginResponse(string Token);
