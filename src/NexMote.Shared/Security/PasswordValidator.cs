namespace NexMote.Shared.Security;

/// <summary>
/// Şifre karmaşıklığı ve giriş doğrulama yardımcısı.
/// Kurumsal güvenlik standartlarına (en az 8 karakter, harf ve rakam/özel karakter) uygunluğu denetler.
/// </summary>
public static class PasswordValidator
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    /// <summary>
    /// Verilen parolanın güvenlik politikalarına uygunluğunu denetler.
    /// </summary>
    /// <param name="password">Denetlenecek parola.</param>
    /// <param name="errorMessage">Hata mesajı (varsa).</param>
    /// <returns>Geçerli ise true, değilse false.</returns>
    public static bool Validate(string? password, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            errorMessage = "Parola boş olamaz.";
            return false;
        }

        if (password.Length < MinLength)
        {
            errorMessage = $"Parola en az {MinLength} karakter uzunluğunda olmalıdır.";
            return false;
        }

        if (password.Length > MaxLength)
        {
            errorMessage = $"Parola en fazla {MaxLength} karakter uzunluğunda olabilir.";
            return false;
        }

        bool hasLetter = false;
        bool hasDigitOrSpecial = false;

        foreach (var c in password)
        {
            if (char.IsLetter(c))
            {
                hasLetter = true;
            }
            else if (char.IsDigit(c) || char.IsPunctuation(c) || char.IsSymbol(c))
            {
                hasDigitOrSpecial = true;
            }
        }

        if (!hasLetter || !hasDigitOrSpecial)
        {
            errorMessage = "Parola en az bir harf ve en az bir rakam veya özel karakter içermelidir.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
