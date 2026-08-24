namespace NexMote.Shared.Contracts;

/// <summary>
/// Web konsolu ve Teknisyen masaüstü uygulamasından sunucuya giriş yapmak için kullanılan istek kontratı (adım 1: e-posta/şifre).
/// </summary>
/// <param name="Email">Kullanıcı e-posta adresi.</param>
/// <param name="Password">Kullanıcı parolası.</param>
public sealed record AdminLoginRequest(string Email, string Password);

/// <summary>
/// Adım 1 (e-posta/şifre) sonrasında sunucunun döndürdüğü yanıt. Kullanıcının MFA'sı kapalıysa
/// <see cref="Token"/> dolu döner (giriş tamamlanmıştır); açıksa <see cref="RequiresMfa"/> true ve
/// <see cref="ChallengeToken"/> dolu döner — asıl oturum token'ı ancak <c>/api/auth/mfa/verify</c> ile alınır.
/// </summary>
public sealed record LoginResponse(bool RequiresMfa, string? Token, string? ChallengeToken);

/// <summary>
/// Adım 2: MFA challenge token'ı ve authenticator uygulamasından okunan 6 haneli kod (veya bir kurtarma kodu).
/// </summary>
public sealed record MfaVerifyRequest(string ChallengeToken, string Code);

/// <summary>
/// Giriş yapmış kullanıcının kimlik/rol bilgisi (<c>/api/auth/me</c>).
/// </summary>
public sealed record CurrentUserResponse(Guid Id, string Email, string DisplayName, string Role, bool MfaEnabled);

/// <summary>Kendi şifresini değiştirme isteği.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>MFA kurulum başlangıcı yanıtı — QR kodu client-side bu <see cref="ProvisioningUri"/>'den üretilir.</summary>
public sealed record MfaSetupResponse(string Secret, string ProvisioningUri);

/// <summary>MFA kurulumunu ilk 6 haneli kodla onaylama isteği.</summary>
public sealed record MfaEnableRequest(string Code);

/// <summary>MFA etkinleştirildiğinde bir kereliğine düz metin dönen kurtarma kodları.</summary>
public sealed record MfaEnableResponse(IReadOnlyList<string> RecoveryCodes);

/// <summary>MFA'yı kapatma isteği — mevcut şifre doğrulaması gerektirir.</summary>
public sealed record MfaDisableRequest(string CurrentPassword);

/// <summary>Admin tarafından yeni kullanıcı (Admin veya Teknisyen) oluşturma isteği.</summary>
public sealed record CreateUserRequest(string Email, string DisplayName, string Role);

/// <summary>Yeni oluşturulan kullanıcı için üretilen tek seferlik geçici şifreyi içeren yanıt.</summary>
public sealed record CreateUserResponse(Guid Id, string Email, string TemporaryPassword);

/// <summary>Kullanıcı yönetimi listesinde gösterilen özet bilgi.</summary>
public sealed record UserSummary(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool IsActive,
    bool MfaEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

/// <summary>Kullanıcının rolünü değiştirme isteği.</summary>
public sealed record SetRoleRequest(string Role);

/// <summary>Admin tarafından yeni kullanıcıyı e-posta ile davet etme isteği.</summary>
public sealed record InviteUserRequest(string Email, string DisplayName, string Role);

/// <summary>Davet kabul ekranının göstereceği önizleme bilgisi.</summary>
public sealed record InvitePreviewResponse(string Email, string DisplayName, string Role);

/// <summary>Davet edilen kişinin kendi şifresini belirleyip daveti kabul etme isteği.</summary>
public sealed record AcceptInviteRequest(string Password);

/// <summary>SMTP test e-postası gönderme isteği.</summary>
public sealed record SmtpTestRequest(string ToEmail);

/// <summary>Denetim logu (Audit Log) tekil kaydı.</summary>
public sealed record ActivityLogEntry(
    Guid Id,
    Guid? UserId,
    string? UserEmail,
    string Action,
    string? TargetType,
    string? TargetId,
    string? DetailsJson,
    string? IpAddress,
    bool Success,
    DateTimeOffset CreatedAt);

/// <summary>
/// Sunucu genel konfigürasyon ayarları (URL, ortak kayıt anahtarı, heartbeat sıklığı, varsayılan lokasyon) kontratı.
/// </summary>
/// <param name="ServerUrl">Sunucunun dışa açık ana erişim adresi (Örn: https://nexmote.com).</param>
/// <param name="EnrollmentKey">Yeni agent'ların kaydolabilmesi için gereken kayıt anahtarı.</param>
/// <param name="HeartbeatSeconds">Heartbeat gönderim periyodu (Saniye).</param>
/// <param name="DefaultLocationCode">Yeni kaydolan cihazlara atanacak varsayılan lokasyon kodu.</param>
/// <param name="TechnicianKey">Teknisyen anahtarı (opsiyonel/geriye uyumluluk).</param>
/// <param name="SmtpHost">SMTP sunucu adresi (örn. smtp.hostinger.com).</param>
/// <param name="SmtpPort">SMTP port (varsayılan 465).</param>
/// <param name="SmtpUsername">SMTP kullanıcı adı.</param>
/// <param name="SmtpPassword">SMTP şifresi — write-only: GET yanıtında her zaman boş döner, POST'ta boş bırakılırsa mevcut şifre korunur.</param>
/// <param name="SmtpFromAddress">Giden e-postalarda "Kimden" adresi.</param>
/// <param name="SmtpFromName">Giden e-postalarda görünen gönderen adı.</param>
public sealed record ServerSettingsContract(
    string ServerUrl,
    string EnrollmentKey,
    int HeartbeatSeconds,
    string DefaultLocationCode,
    string TechnicianKey = "",
    string? SmtpHost = null,
    int SmtpPort = 465,
    string? SmtpUsername = null,
    string? SmtpPassword = null,
    string? SmtpFromAddress = null,
    string? SmtpFromName = null,
    bool AlertsEnabled = true,
    string? AlertRecipientEmails = null,
    bool AlertOfflineEnabled = true,
    int AlertOfflineMinutes = 5,
    bool AlertDiskLowEnabled = true,
    int AlertDiskLowMb = 5000,
    bool AlertCpuHighEnabled = false,
    double AlertCpuHighPercent = 90,
    bool AlertMemoryHighEnabled = false,
    double AlertMemoryHighPercent = 90);
