using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;

namespace NexMote.Api.Services;

public sealed class EnrollmentKeyValidator
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IConfiguration _config;

    public EnrollmentKeyValidator(IDbContextFactory<AppDbContext> dbFactory, IConfiguration config)
    {
        _dbFactory = dbFactory;
        _config = config;
    }

    public EnrollmentAuthorizationResult Authorize(string? enrollmentKey)
    {
        if (string.IsNullOrWhiteSpace(enrollmentKey))
        {
            return EnrollmentAuthorizationResult.Denied;
        }

        var provided = enrollmentKey.Trim();

        // 1. Sistem konfigürasyonu / ortam değişkeni kontrolü (Windows Ortam Değişkenleri veya appsettings Enrollment:Key)
        var configKey = _config["Enrollment:Key"];
        if (!string.IsNullOrWhiteSpace(configKey) && SecretEquals(configKey, provided))
        {
            return new EnrollmentAuthorizationResult(true, null);
        }

        using var db = _dbFactory.CreateDbContext();

        // 2. Veritabanı ServerSettings tablosundaki global anahtar kontrolü
        var globalKey = db.ServerSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => s.EnrollmentKey)
            .FirstOrDefault();

        if (SecretEquals(globalKey, provided))
        {
            return new EnrollmentAuthorizationResult(true, null);
        }

        // 3. Şirket / Departman grup bazlı kayıt anahtarları kontrolü
        var group = db.DeviceGroups
            .AsNoTracking()
            .Where(g => g.EnrollmentKey != null)
            .ToList()
            .FirstOrDefault(g => SecretEquals(g.EnrollmentKey, provided));

        return group is null
            ? EnrollmentAuthorizationResult.Denied
            : new EnrollmentAuthorizationResult(true, group.Id);
    }

    private static bool SecretEquals(string? stored, string provided)
    {
        if (string.IsNullOrWhiteSpace(stored) || string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var trimmedStored = stored.Trim();
        var trimmedProvided = provided.Trim();

        // 1. SHA-256 hash karşılaştırması (DB'de hash saklanıyorsa)
        var providedHash = SessionTokens.Hash(trimmedProvided);
        if (TimingSafeCompare(trimmedStored, providedHash))
        {
            return true;
        }

        // 2. Geriye uyumluluk: düz metin karşılaştırması
        return TimingSafeCompare(trimmedStored, trimmedProvided);
    }

    private static bool TimingSafeCompare(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        if (aBytes.Length != bBytes.Length)
        {
            CryptographicOperations.FixedTimeEquals(aBytes, aBytes);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}

public sealed record EnrollmentAuthorizationResult(bool IsAuthorized, Guid? GroupId)
{
    public static EnrollmentAuthorizationResult Denied { get; } = new(false, null);
}
