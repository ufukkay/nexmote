using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

/// <summary>
/// Teknisyenlerin cihazlara canlı uzaktan masaüstü bağlantısı kurabilmesi için geçici oturumları ve "nexmote://" deep-link'lerini yöneten servis.
/// </summary>
public sealed class RemoteSessionRegistry
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ActiveSessionLifetime = TimeSpan.FromHours(8);

    public RemoteSessionRegistry(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Belirtilen hedef cihaz için 5 dakika geçerli benzersiz bir davet ve "nexmote://" deep-link başlatma URL'i üretir.
    /// </summary>
    /// <param name="deviceId">Hedef cihaz kimliği.</param>
    /// <param name="serverUrl">Sunucu genel URL'i.</param>
    /// <returns>Oturum ID, token ve LaunchUri içeren yanıt.</returns>
    public CreateRemoteSessionResponse Create(Guid deviceId, string serverUrl, Guid ownerUserId, string? userToken = null)
    {
        using var db = _dbFactory.CreateDbContext();

        var id = Guid.NewGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(InviteLifetime);

        var entity = new RemoteSessionEntity
        {
            Id = id,
            DeviceId = deviceId,
            Token = HashToken(token),
            OwnerUserId = ownerUserId,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        db.RemoteSessions.Add(entity);
        db.SaveChanges();

        var normalizedServerUrl = serverUrl.TrimEnd('/');
        if (Uri.TryCreate(normalizedServerUrl, UriKind.Absolute, out var parsedUri) &&
            (parsedUri.Host.Equals("nexmote.com", StringComparison.OrdinalIgnoreCase) ||
             parsedUri.Host.Equals("www.nexmote.com", StringComparison.OrdinalIgnoreCase) ||
             parsedUri.Host.EndsWith(".nexmote.com", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedServerUrl = "https://nexmote.com";
        }

        // Teknisyen masaüstü uygulamasını tetikleyen custom URI protokol formatı
        var launchUri = $"nexmote://connect?sessionId={id}&token={Uri.EscapeDataString(token)}&serverUrl={Uri.EscapeDataString(normalizedServerUrl)}&deviceId={deviceId}";
        if (!string.IsNullOrWhiteSpace(userToken))
        {
            launchUri += $"&userToken={Uri.EscapeDataString(userToken)}";
        }
        return new CreateRemoteSessionResponse(id, deviceId, launchUri, expiresAt);
    }

    /// <summary>
    /// Belirtilen oturum kimliğine ait kaydı getirir. Süresi dolmuşsa null döner.
    /// </summary>
    /// <param name="sessionId">Oturum kimliği.</param>
    public RemoteSessionRecord? Get(Guid sessionId)
    {
        using var db = _dbFactory.CreateDbContext();

        var session = db.RemoteSessions.AsNoTracking().FirstOrDefault(s => s.Id == sessionId);
        if (session is null || session.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return new RemoteSessionRecord(session.Id, session.DeviceId, string.Empty, session.ExpiresAt);
    }

    public RemoteSessionRecord? Activate(Guid sessionId, string token, Guid? ownerUserId = null)
    {
        using var db = _dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();

        var session = db.RemoteSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null ||
            session.ExpiresAt <= DateTimeOffset.UtcNow ||
            (session.OwnerUserId.HasValue && ownerUserId.HasValue && session.OwnerUserId.Value != ownerUserId.Value))
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        string activeToken;
        if (session.ActivatedAt is null)
        {
            if (!TokenMatches(session.Token, token))
            {
                return null;
            }

            activeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            session.Token = string.Empty;
            session.ActiveTokenHash = HashToken(activeToken);
            session.ActivatedAt = now;
        }
        else
        {
            if (!TokenMatches(session.ActiveTokenHash, token))
            {
                return null;
            }

            activeToken = token;
        }

        session.ExpiresAt = now.Add(ActiveSessionLifetime);
        db.SaveChanges();
        transaction.Commit();

        return new RemoteSessionRecord(session.Id, session.DeviceId, activeToken, session.ExpiresAt);
    }

    public void Expire(Guid sessionId)
    {
        using var db = _dbFactory.CreateDbContext();
        var session = db.RemoteSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null)
        {
            session.ExpiresAt = DateTimeOffset.UtcNow;
            session.ActiveTokenHash = null;
            db.SaveChanges();
        }
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool TokenMatches(string? storedHash, string token)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        var actualHash = Convert.FromHexString(HashToken(token));
        var expectedHash = Convert.FromHexString(storedHash);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

/// <summary>
/// Bellek içi ve oturum sorgularında kullanılan aktif oturum kaydı.
/// </summary>
public sealed record RemoteSessionRecord(Guid Id, Guid DeviceId, string Token, DateTimeOffset ExpiresAt);
