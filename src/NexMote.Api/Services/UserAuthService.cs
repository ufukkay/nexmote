using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Çoklu kullanıcı (Admin/Teknisyen) girişi, MFA (TOTP) kurulum/doğrulama, oturum yönetimi,
/// kullanıcı yönetimi (admin-only) ve denetim (activity) logu işlemlerini yürüten servis.
/// </summary>
public sealed class UserAuthService
{
    private const string MfaProtectorPurpose = "NexMote.Api.MfaSecret.v1";
    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan SessionLifetimeRemembered = TimeSpan.FromDays(30);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPasswordHasher<UserEntity> _passwordHasher;
    private readonly TotpService _totp;
    private readonly IDataProtector _mfaProtector;

    public UserAuthService(
        IDbContextFactory<AppDbContext> dbFactory,
        IPasswordHasher<UserEntity> passwordHasher,
        TotpService totp,
        IDataProtectionProvider dataProtectionProvider)
    {
        _dbFactory = dbFactory;
        _passwordHasher = passwordHasher;
        _totp = totp;
        _mfaProtector = dataProtectionProvider.CreateProtector(MfaProtectorPurpose);
    }

    // ----------------------------------------------------------------- Bootstrap

    /// <summary>
    /// İlk açılışta <c>Users</c> tablosu boşsa, mevcut Admin:Email/Admin:Password konfigürasyonundan
    /// tek bir Admin kullanıcısı seed eder — böylece var olan production sunucusu koptan geçiş yapar.
    /// </summary>
    public static void EnsureBootstrapAdmin(AppDbContext db, IPasswordHasher<UserEntity> hasher, string email, string password)
    {
        if (db.Users.Any())
        {
            return;
        }

        var admin = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            DisplayName = "Yönetici",
            Role = UserRoles.Admin,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);

        db.Users.Add(admin);
        db.SaveChanges();
    }

    // ----------------------------------------------------------------- Login (adım 1 + adım 2)

    public LoginResponse? LoginStep1(string email, string password, bool rememberMe, string? ip, string? userAgent)
    {
        using var db = _dbFactory.CreateDbContext();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = db.Users.FirstOrDefault(u => u.Email == normalizedEmail);

        if (user is null || !user.IsActive)
        {
            LogActivity(db, null, normalizedEmail, "login.failed", null, null, null, ip, success: false);
            return null;
        }

        var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            LogActivity(db, user.Id, user.Email, "login.failed", null, null, null, ip, success: false);
            return null;
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _passwordHasher.HashPassword(user, password);
        }

        if (user.MfaEnabled)
        {
            var challengeToken = SessionTokens.Generate();
            db.UserSessions.Add(new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TokenHash = SessionTokens.Hash(challengeToken),
                IsMfaPending = true,
                CreatedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.Add(MfaChallengeLifetime),
                IpAddress = ip,
                UserAgent = Truncate(userAgent, 256)
            });
            LogActivity(db, user.Id, user.Email, "login.mfa_challenge", null, null, null, ip, success: true);
            db.SaveChanges();
            return new LoginResponse(RequiresMfa: true, Token: null, ChallengeToken: challengeToken);
        }

        var token = IssueSession(db, user, rememberMe, ip, userAgent);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        LogActivity(db, user.Id, user.Email, "login.success", null, null, null, ip, success: true);
        db.SaveChanges();
        return new LoginResponse(RequiresMfa: false, Token: token, ChallengeToken: null);
    }

    public LoginResponse? VerifyMfaStep2(string challengeToken, string code, bool rememberMe, string? ip, string? userAgent)
    {
        using var db = _dbFactory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var challengeHash = SessionTokens.Hash(challengeToken);
        var pending = db.UserSessions.FirstOrDefault(s => s.TokenHash == challengeHash);

        if (pending is null || !pending.IsMfaPending || pending.RevokedAt != null || pending.ExpiresAt <= now)
        {
            return null;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == pending.UserId);
        if (user is null || !user.IsActive || !user.MfaEnabled || string.IsNullOrEmpty(user.MfaSecretEncrypted))
        {
            return null;
        }

        var isValid = _totp.VerifyCode(Unprotect(user.MfaSecretEncrypted), code) || TryConsumeRecoveryCode(db, user, code);
        if (!isValid)
        {
            LogActivity(db, user.Id, user.Email, "login.mfa_failed", null, null, null, ip, success: false);
            db.SaveChanges();
            return null;
        }

        pending.RevokedAt = now;
        var token = IssueSession(db, user, rememberMe, ip, userAgent);
        user.LastLoginAt = now;
        LogActivity(db, user.Id, user.Email, "login.success", null, null, null, ip, success: true);
        db.SaveChanges();
        return new LoginResponse(RequiresMfa: false, Token: token, ChallengeToken: null);
    }

    public void Logout(string token, string? ip)
    {
        using var db = _dbFactory.CreateDbContext();
        var hash = SessionTokens.Hash(token);
        var session = db.UserSessions.FirstOrDefault(s => s.TokenHash == hash);
        if (session is null || session.IsMfaPending || session.RevokedAt != null)
        {
            return;
        }

        session.RevokedAt = DateTimeOffset.UtcNow;
        LogActivity(db, session.UserId, null, "logout", null, null, null, ip, success: true);
        db.SaveChanges();
    }

    private static string IssueSession(AppDbContext db, UserEntity user, bool rememberMe, string? ip, string? userAgent)
    {
        var token = SessionTokens.Generate();
        db.UserSessions.Add(new UserSessionEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            IsMfaPending = false,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.Add(rememberMe ? SessionLifetimeRemembered : SessionLifetime),
            IpAddress = ip,
            UserAgent = Truncate(userAgent, 256)
        });
        return token;
    }

    // ----------------------------------------------------------------- Hesap (kendi şifre/MFA yönetimi)

    public bool ChangePassword(Guid userId, string currentPassword, string newPassword)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
        LogActivity(db, user.Id, user.Email, "account.password_change", null, null, null, null, success: true);
        db.SaveChanges();
        return true;
    }

    public MfaSetupResponse SetupMfa(Guid userId)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.First(u => u.Id == userId);
        var secret = _totp.GenerateSecret();
        user.MfaSecretEncrypted = Protect(secret);
        db.SaveChanges();
        return new MfaSetupResponse(secret, _totp.BuildProvisioningUri(user.Email, secret));
    }

    public MfaEnableResponse? EnableMfa(Guid userId, string code)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null || string.IsNullOrEmpty(user.MfaSecretEncrypted))
        {
            return null;
        }

        if (!_totp.VerifyCode(Unprotect(user.MfaSecretEncrypted), code))
        {
            return null;
        }

        var recoveryCodes = Enumerable.Range(0, 10).Select(_ => GenerateRecoveryCode()).ToList();
        user.MfaEnabled = true;
        user.MfaRecoveryCodesHashJson = JsonSerializer.Serialize(recoveryCodes.Select(HashRecoveryCode).ToList());
        LogActivity(db, user.Id, user.Email, "account.mfa_enabled", null, null, null, null, success: true);
        db.SaveChanges();
        return new MfaEnableResponse(recoveryCodes);
    }

    public bool DisableMfa(Guid userId, string currentPassword)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
        {
            return false;
        }

        user.MfaEnabled = false;
        user.MfaSecretEncrypted = null;
        user.MfaRecoveryCodesHashJson = null;
        LogActivity(db, user.Id, user.Email, "account.mfa_disabled", null, null, null, null, success: true);
        db.SaveChanges();
        return true;
    }

    // ----------------------------------------------------------------- Kullanıcı yönetimi (Admin-only)

    public IReadOnlyList<UserSummary> ListUsers()
    {
        using var db = _dbFactory.CreateDbContext();
        return db.Users
            .OrderBy(u => u.Email)
            .Select(u => new UserSummary(u.Id, u.Email, u.DisplayName, u.Role, u.IsActive, u.MfaEnabled, u.CreatedAt, u.LastLoginAt))
            .ToList();
    }

    public CreateUserResponse? CreateUser(string email, string displayName, string role, Guid actingUserId)
    {
        if (role != UserRoles.Admin && role != UserRoles.Technician)
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (db.Users.Any(u => u.Email == normalizedEmail))
        {
            return null;
        }

        var tempPassword = GenerateTemporaryPassword();
        var user = new UserEntity
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim(),
            Role = role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);

        db.Users.Add(user);
        LogActivity(db, actingUserId, null, "user.create", "User", user.Id.ToString(), $"role={role}", null, success: true);
        db.SaveChanges();
        return new CreateUserResponse(user.Id, user.Email, tempPassword);
    }

    /// <summary>
    /// Yeni bir kullanıcı için (yoksa oluşturarak) e-posta davet token'ı üretir. E-posta göndermez —
    /// çağıran (endpoint) döndürülen token'ı <see cref="EmailService"/> ile göndermelidir.
    /// Aynı e-postaya daha önce kabul edilmemiş bir davet varsa, eskisi geçersiz kılınıp yenisi üretilir
    /// (yeniden gönderim). E-posta zaten gerçek (kabul edilmiş veya şifre-ile-oluşturulmuş) bir hesaba
    /// aitse null döner.
    /// </summary>
    public (Guid UserId, string Email, string DisplayName, string Role, string Token)? InviteUser(string email, string displayName, string role, Guid actingUserId)
    {
        if (role != UserRoles.Admin && role != UserRoles.Technician)
        {
            return null;
        }

        using var db = _dbFactory.CreateDbContext();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var existing = db.Users.FirstOrDefault(u => u.Email == normalizedEmail);

        UserEntity user;
        if (existing is null)
        {
            user = new UserEntity
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalizedEmail : displayName.Trim(),
                Role = role,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            // Kabul edilene kadar tahmin edilemez, kullanılamaz bir şifre hash'i (nullable kolon gerektirmez)
            user.PasswordHash = _passwordHasher.HashPassword(user, SessionTokens.Generate());
            db.Users.Add(user);
        }
        else
        {
            var hasAnyInvite = db.UserInvites.Any(i => i.UserId == existing.Id);
            var hasAcceptedInvite = db.UserInvites.Any(i => i.UserId == existing.Id && i.AcceptedAt != null);
            if (!hasAnyInvite || hasAcceptedInvite)
            {
                // Şifreyle doğrudan oluşturulmuş ya da daveti zaten kabul edilmiş gerçek bir hesap — üzerine yazılmaz.
                return null;
            }

            user = existing;
            user.Role = role;
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                user.DisplayName = displayName.Trim();
            }
        }

        // Bu kullanıcı için bekleyen (kabul edilmemiş) eski davetleri geçersiz kıl — yeniden gönderim senaryosu
        var now = DateTimeOffset.UtcNow;
        foreach (var old in db.UserInvites.Where(i => i.UserId == user.Id && i.AcceptedAt == null))
        {
            old.ExpiresAt = now;
        }

        var token = SessionTokens.Generate();
        db.UserInvites.Add(new UserInviteEntity
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = SessionTokens.Hash(token),
            CreatedAt = now,
            ExpiresAt = now.AddDays(7),
            InvitedByUserId = actingUserId
        });

        LogActivity(db, actingUserId, null, "user.invite", "User", user.Id.ToString(), $"role={role}", null, success: true);
        db.SaveChanges();
        return (user.Id, user.Email, user.DisplayName, user.Role, token);
    }

    /// <summary>Geçerli (süresi dolmamış, kabul edilmemiş) bir davetin önizleme bilgisini döner.</summary>
    public (string Email, string DisplayName, string Role)? GetInvitePreview(string token)
    {
        using var db = _dbFactory.CreateDbContext();
        var hash = SessionTokens.Hash(token);
        var invite = db.UserInvites.FirstOrDefault(i => i.TokenHash == hash);
        if (invite is null || invite.AcceptedAt != null || invite.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == invite.UserId);
        return user is null ? null : (user.Email, user.DisplayName, user.Role);
    }

    /// <summary>
    /// Daveti kabul eder: kullanıcının gerçek şifresini ayarlar, daveti "kullanılmış" işaretler
    /// ve otomatik olarak bir oturum açar (kabul eden kişi doğrudan uygulamaya giriş yapmış olur).
    /// </summary>
    public LoginResponse? AcceptInvite(string token, string password, string? ip, string? userAgent)
    {
        using var db = _dbFactory.CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var hash = SessionTokens.Hash(token);
        var invite = db.UserInvites.FirstOrDefault(i => i.TokenHash == hash);
        if (invite is null || invite.AcceptedAt != null || invite.ExpiresAt <= now)
        {
            return null;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == invite.UserId);
        if (user is null || !user.IsActive)
        {
            return null;
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, password);
        user.LastLoginAt = now;
        invite.AcceptedAt = now;

        var sessionToken = IssueSession(db, user, rememberMe: true, ip, userAgent);
        LogActivity(db, user.Id, user.Email, "user.invite_accepted", null, null, null, ip, success: true);
        db.SaveChanges();
        return new LoginResponse(RequiresMfa: false, Token: sessionToken, ChallengeToken: null);
    }

    public bool SetRole(Guid userId, string role, Guid actingUserId)
    {
        if (role != UserRoles.Admin && role != UserRoles.Technician)
        {
            return false;
        }

        if (userId == actingUserId && role != UserRoles.Admin)
        {
            // Kendi rolünü Admin'den düşürmek, tek admin'se yönetim ekranlarına erişimi
            // kaybetmesine yol açabilir — sunucu tarafında engellenir.
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        user.Role = role;
        LogActivity(db, actingUserId, null, "user.role_change", "User", user.Id.ToString(), $"role={role}", null, success: true);
        db.SaveChanges();
        return true;
    }

    public bool SetActive(Guid userId, bool active, Guid actingUserId)
    {
        if (!active && userId == actingUserId)
        {
            // Bir kullanıcının kendi hesabını devre dışı bırakması, tek admin'se sistemden
            // kimsenin çıkaramayacağı bir kilitlenmeye yol açabilir — sunucu tarafında engellenir.
            return false;
        }

        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        user.IsActive = active;
        if (!active)
        {
            // Devre dışı bırakılan kullanıcının tüm aktif oturumlarını anında iptal et
            foreach (var session in db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null))
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
            }
        }

        LogActivity(db, actingUserId, null, active ? "user.enable" : "user.disable", "User", user.Id.ToString(), null, null, success: true);
        db.SaveChanges();
        return true;
    }

    public bool AdminResetMfa(Guid userId, Guid actingUserId)
    {
        using var db = _dbFactory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return false;
        }

        user.MfaEnabled = false;
        user.MfaSecretEncrypted = null;
        user.MfaRecoveryCodesHashJson = null;
        LogActivity(db, actingUserId, null, "user.mfa_reset", "User", user.Id.ToString(), null, null, success: true);
        db.SaveChanges();
        return true;
    }

    // ----------------------------------------------------------------- Denetim Logu (Audit Log)

    public void LogActivity(Guid? userId, string? action, string? targetType, string? targetId, string? details, string? ip, bool success)
    {
        using var db = _dbFactory.CreateDbContext();
        LogActivity(db, userId, null, action ?? "unknown", targetType, targetId, details, ip, success);
        db.SaveChanges();
    }

    private static void LogActivity(AppDbContext db, Guid? userId, string? emailSnapshot, string action, string? targetType, string? targetId, string? details, string? ip, bool success)
    {
        string? resolvedEmail = emailSnapshot;
        if (resolvedEmail is null && userId.HasValue)
        {
            resolvedEmail = db.Users.Where(u => u.Id == userId.Value).Select(u => u.Email).FirstOrDefault();
        }

        db.ActivityLogs.Add(new ActivityLogEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            UserEmailSnapshot = resolvedEmail,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            DetailsJson = details,
            IpAddress = ip,
            Success = success,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public (IReadOnlyList<ActivityLogEntry> Items, int Total) GetAuditLog(int page, int pageSize, Guid? userId, string? action)
    {
        using var db = _dbFactory.CreateDbContext();
        var query = db.ActivityLogs.AsQueryable();
        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        // SQLite/EF Core, DateTimeOffset kolonlarını ORDER BY içinde SQL'e çeviremiyor (bkz. DeviceRegistry.List()
        // aynı desen) — eşleşen satırlar önce materialize edilip sıralama/sayfalama client-side yapılır.
        var all = query.ToList();
        var total = all.Count;
        var items = all
            .OrderByDescending(a => a.CreatedAt)
            .Skip(Math.Max(0, page - 1) * pageSize)
            .Take(Math.Clamp(pageSize, 1, 200))
            .Select(a => new ActivityLogEntry(a.Id, a.UserId, a.UserEmailSnapshot, a.Action, a.TargetType, a.TargetId, a.DetailsJson, a.IpAddress, a.Success, a.CreatedAt))
            .ToList();

        return (items, total);
    }

    // ----------------------------------------------------------------- Yardımcılar

    private string Protect(string plainText) => _mfaProtector.Protect(plainText);

    private string Unprotect(string cipherText) => _mfaProtector.Unprotect(cipherText);

    private bool TryConsumeRecoveryCode(AppDbContext db, UserEntity user, string code)
    {
        if (string.IsNullOrWhiteSpace(user.MfaRecoveryCodesHashJson))
        {
            return false;
        }

        var hashes = JsonSerializer.Deserialize<List<string>>(user.MfaRecoveryCodesHashJson) ?? [];
        var codeHash = HashRecoveryCode(code);
        var index = hashes.IndexOf(codeHash);
        if (index < 0)
        {
            return false;
        }

        hashes.RemoveAt(index);
        user.MfaRecoveryCodesHashJson = JsonSerializer.Serialize(hashes);
        return true;
    }

    private static string GenerateRecoveryCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(5);
        var hex = Convert.ToHexString(bytes);
        return $"{hex[..5]}-{hex[5..]}";
    }

    private static string HashRecoveryCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant())));

    private static string GenerateTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(14);
        var chars = bytes.Select(b => alphabet[b % alphabet.Length]).ToArray();
        return new string(chars);
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= maxLength ? value : value[..maxLength];
}
