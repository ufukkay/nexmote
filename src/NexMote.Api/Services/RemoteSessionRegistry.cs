using System.Security.Cryptography;
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
    public CreateRemoteSessionResponse Create(Guid deviceId, string serverUrl)
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
            Token = token,
            CreatedAt = now,
            ExpiresAt = expiresAt
        };

        db.RemoteSessions.Add(entity);
        db.SaveChanges();

        // Teknisyen masaüstü uygulamasını tetikleyen custom URI protokol formatı
        var launchUri = $"nexmote://connect?sessionId={id}&token={Uri.EscapeDataString(token)}&serverUrl={Uri.EscapeDataString(serverUrl.TrimEnd('/'))}&deviceId={deviceId}";
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

        return new RemoteSessionRecord(session.Id, session.DeviceId, session.Token, session.ExpiresAt);
    }

    public RemoteSessionRecord? Activate(Guid sessionId, string token)
    {
        using var db = _dbFactory.CreateDbContext();

        var session = db.RemoteSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null ||
            session.ExpiresAt <= DateTimeOffset.UtcNow ||
            !string.Equals(session.Token, token, StringComparison.Ordinal))
        {
            return null;
        }

        session.ExpiresAt = DateTimeOffset.UtcNow.Add(ActiveSessionLifetime);
        db.SaveChanges();

        return new RemoteSessionRecord(session.Id, session.DeviceId, session.Token, session.ExpiresAt);
    }

    public void Expire(Guid sessionId)
    {
        using var db = _dbFactory.CreateDbContext();
        var session = db.RemoteSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is not null)
        {
            session.ExpiresAt = DateTimeOffset.UtcNow;
            db.SaveChanges();
        }
    }
}
