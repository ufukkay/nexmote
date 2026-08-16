namespace NexMote.Shared.Contracts;

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
