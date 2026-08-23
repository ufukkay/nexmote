using OtpNet;

namespace NexMote.Api.Auth;

/// <summary>
/// RFC 6238 TOTP (authenticator uygulaması tabanlı) MFA secret üretimi, provisioning URI'si ve kod doğrulaması.
/// </summary>
public sealed class TotpService
{
    /// <summary>Yeni, kriptografik olarak rastgele bir base32 TOTP secret'ı üretir.</summary>
    public string GenerateSecret()
    {
        var key = KeyGeneration.GenerateRandomKey(20);
        return Base32Encoding.ToString(key);
    }

    /// <summary>
    /// Authenticator uygulamasının QR ile okuyabileceği <c>otpauth://</c> provisioning URI'sini üretir.
    /// QR görseli sunucuda üretilmez, client-side (web konsolu) bu URI'den render edilir.
    /// </summary>
    public string BuildProvisioningUri(string email, string base32Secret)
    {
        var label = Uri.EscapeDataString($"NexMote:{email}");
        return $"otpauth://totp/{label}?secret={base32Secret}&issuer=NexMote&digits=6&period=30";
    }

    /// <summary>
    /// 6 haneli kodu, saat kaymasına toleranslı olacak şekilde (±1 zaman adımı) doğrular.
    /// </summary>
    public bool VerifyCode(string base32Secret, string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var totp = new Totp(Base32Encoding.ToBytes(base32Secret));
        return totp.VerifyTotp(code.Trim(), out _, new VerificationWindow(previous: 1, future: 1));
    }
}
