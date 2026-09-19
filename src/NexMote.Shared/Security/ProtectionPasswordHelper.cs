using Microsoft.AspNetCore.Identity;

namespace NexMote.Shared.Security;

/// <summary>
/// Kurumsal Ajan Koruma Şifresini PBKDF2 standardında güvenle hash'leyen ve hem sunucuda hem ajanda (offline dahil) doğrulayan yardımcı sınıf.
/// </summary>
public static class ProtectionPasswordHelper
{
    private static readonly PasswordHasher<object> Hasher = new();
    private static readonly object Dummy = new();

    public static string Hash(string password)
    {
        return Hasher.HashPassword(Dummy, password);
    }

    public static bool Verify(string? hash, string? password)
    {
        if (string.IsNullOrWhiteSpace(hash) || string.IsNullOrEmpty(password))
        {
            return false;
        }

        try
        {
            var result = Hasher.VerifyHashedPassword(Dummy, hash, password);
            return result != PasswordVerificationResult.Failed;
        }
        catch
        {
            return false;
        }
    }
}
