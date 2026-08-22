namespace NexMote.Shared.Contracts;

/// <summary>
/// Web konsolu ve Teknisyen masaüstü uygulamasından sunucuya admin girişi yapmak için kullanılan istek kontratı.
/// </summary>
/// <param name="Email">Admin kullanıcı e-posta adresi.</param>
/// <param name="Password">Admin kullanıcı parolası.</param>
public sealed record AdminLoginRequest(string Email, string Password);

/// <summary>
/// Başarılı admin girişi sonrasında sunucunun döndürdüğü kimlik doğrulama yanıtı.
/// </summary>
/// <param name="Token">Korumalı admin API isteklerinde "Authorization: Bearer [Token]" olarak kullanılacak anahtar.</param>
public sealed record AdminLoginResponse(string Token);

/// <summary>
/// Sunucu genel konfigürasyon ayarları (URL, ortak kayıt anahtarı, heartbeat sıklığı, varsayılan lokasyon) kontratı.
/// </summary>
/// <param name="ServerUrl">Sunucunun dışa açık ana erişim adresi (Örn: https://nexmote.com).</param>
/// <param name="EnrollmentKey">Yeni agent'ların kaydolabilmesi için gereken kayıt anahtarı.</param>
/// <param name="HeartbeatSeconds">Heartbeat gönderim periyodu (Saniye).</param>
/// <param name="DefaultLocationCode">Yeni kaydolan cihazlara atanacak varsayılan lokasyon kodu.</param>
/// <param name="TechnicianKey">Teknisyen anahtarı (opsiyonel/geriye uyumluluk).</param>
public sealed record ServerSettingsContract(
    string ServerUrl,
    string EnrollmentKey,
    int HeartbeatSeconds,
    string DefaultLocationCode,
    string TechnicianKey = "");
