using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

public sealed class RemoteSessionRegistry
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public RemoteSessionRegistry(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public CreateRemoteSessionResponse Create(Guid deviceId, string serverUrl)
    {
        using var db = _dbFactory.CreateDbContext();

        var id = Guid.NewGuid();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(5);

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

        var launchUri = $"nexmote://connect?sessionId={id}&token={Uri.EscapeDataString(token)}&serverUrl={Uri.EscapeDataString(serverUrl.TrimEnd('/'))}";
        return new CreateRemoteSessionResponse(id, deviceId, launchUri, expiresAt);
    }

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
}
