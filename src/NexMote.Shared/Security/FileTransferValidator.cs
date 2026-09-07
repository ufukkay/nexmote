using System.IO;
using System.Security.Cryptography;

namespace NexMote.Shared.Security;

/// <summary>
/// Dosya aktarımı için güvenlik kurallarını, boyut limitlerini, dosya adı temizleme
/// (path traversal koruması) ve SHA-256 bütünlük kontrollerini yöneten yardımcı sınıf.
/// </summary>
public static class FileTransferValidator
{
    /// <summary>Maksimum transfer edilebilir dosya boyutu (500 MB).</summary>
    public const long MaxFileSizeBytes = 500 * 1024 * 1024;

    /// <summary>Maksimum tekil parça (chunk) boyutu (1 MB).</summary>
    public const int MaxChunkSizeBytes = 1 * 1024 * 1024;

    /// <summary>Aktif olmayan dosya transferinin zaman aşımı süresi (5 dakika).</summary>
    public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Dosya adını temizleyerek dizin geçişi (path traversal - ../) ve geçersiz karakter saldırılarını engeller.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "transfer.bin";
        }

        // 1. Sadece dosya adını al (Dizin yollarını tamamen sıyır)
        var nameOnly = Path.GetFileName(fileName);

        // 2. Geçersiz dosya adı karakterlerini alt çizgi ile değiştir
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(nameOnly.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim();

        // 3. Gizli veya boş isim kontrolü
        if (string.IsNullOrWhiteSpace(sanitized) || sanitized == "." || sanitized == "..")
        {
            return "transfer.bin";
        }

        // 4. Uzunluk sınırlaması (En fazla 128 karakter)
        if (sanitized.Length > 128)
        {
            var ext = Path.GetExtension(sanitized);
            var baseName = Path.GetFileNameWithoutExtension(sanitized);
            var maxBaseLen = Math.Max(1, 128 - ext.Length);
            sanitized = baseName[..Math.Min(baseName.Length, maxBaseLen)] + ext;
        }

        return sanitized;
    }

    /// <summary>
    /// Bir akışın (Stream) SHA-256 kriptografik özetini hesaplar.
    /// </summary>
    public static string ComputeStreamSha256(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// Beklenen SHA-256 özeti ile hesaplanan özeti büyük/küçük harf duyarsız ve güvenli karşılaştırır.
    /// </summary>
    public static bool VerifyChecksum(string actualHash, string? expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            return true; // Checksum sağlanmadıysa (geriye dönük uyumluluk)
        }

        return string.Equals(actualHash.Trim(), expectedHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
