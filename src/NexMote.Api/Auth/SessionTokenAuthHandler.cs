using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NexMote.Api.Data;

namespace NexMote.Api.Auth;

/// <summary>
/// "Authorization: Bearer {token}" başlığındaki opak oturum token'ını <see cref="Data.UserSessionEntity"/>
/// tablosunda hash'i üzerinden doğrulayıp, kullanıcı kimliği/rolü içeren bir <see cref="ClaimsPrincipal"/> üretir.
/// Eski statik "Admin:ApiKey" karşılaştırmasının (<c>AdminAuthFilter</c>) yerini alır.
/// </summary>
public sealed class SessionTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "NexMoteSession";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SessionTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IDbContextFactory<AppDbContext> dbFactory) : base(options, logger, encoder)
    {
        _dbFactory = dbFactory;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = header["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var tokenHash = SessionTokens.Hash(token);

        using var db = _dbFactory.CreateDbContext();
        var session = db.UserSessions.FirstOrDefault(s => s.TokenHash == tokenHash);

        if (session is null || session.IsMfaPending || session.RevokedAt != null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Task.FromResult(AuthenticateResult.Fail("Geçersiz veya süresi dolmuş oturum."));
        }

        var user = db.Users.FirstOrDefault(u => u.Id == session.UserId);
        if (user is null || !user.IsActive)
        {
            return Task.FromResult(AuthenticateResult.Fail("Kullanıcı bulunamadı veya devre dışı."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("display_name", user.DisplayName),
            new Claim("mfa_enabled", user.MfaEnabled ? "true" : "false")
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>Opak kullanıcı oturum token'larının üretimi ve hash'lenmesi için ortak yardımcı.</summary>
public static class SessionTokens
{
    /// <summary>32-byte kriptografik olarak güçlü rastgele hex token üretir (mevcut Agent token deseniyle aynı).</summary>
    public static string Generate() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Token'ın SHA-256 hash'ini döner — düz metin token asla veritabanına yazılmaz.</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
