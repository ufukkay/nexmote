using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Services;

public sealed class DeviceCommandQueue
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DeviceCommandQueue(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public DeviceCommandEntity Enqueue(
        Guid requestId,
        Guid deviceId,
        string kind,
        string shell,
        string command,
        int timeoutSeconds,
        Guid? initiatorUserId = null,
        string? initiatorEmail = null,
        string? correlationId = null)
    {
        using var db = _dbFactory.CreateDbContext();
        var entity = new DeviceCommandEntity
        {
            Id = Guid.NewGuid(),
            RequestId = requestId,
            DeviceId = deviceId,
            Kind = Truncate(kind, 32),
            Shell = Truncate(shell, 32),
            Command = Truncate(command, 8000),
            TimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 600),
            Status = DeviceCommandStatuses.Queued,
            CreatedAt = DateTimeOffset.UtcNow,
            InitiatorUserId = initiatorUserId,
            InitiatorEmail = Truncate(initiatorEmail, 256),
            CorrelationId = Truncate(correlationId, 64)
        };

        db.DeviceCommands.Add(entity);
        db.SaveChanges();
        return entity;
    }

    public AgentQueuedCommand? TakeNext(Guid deviceId)
    {
        using var db = _dbFactory.CreateDbContext();
        var entity = db.DeviceCommands
            .Where(c => c.DeviceId == deviceId && c.Status == DeviceCommandStatuses.Queued)
            .ToList()
            .OrderBy(c => c.CreatedAt)
            .FirstOrDefault();

        if (entity is null)
        {
            return null;
        }

        entity.Status = DeviceCommandStatuses.Delivered;
        entity.DeliveredAt = DateTimeOffset.UtcNow;
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            var idUpper = entity.Id.ToString().ToUpperInvariant();
            var idLower = entity.Id.ToString().ToLowerInvariant();
            var nowStr = DateTimeOffset.UtcNow.ToString("O");
            db.Database.ExecuteSqlRaw(
                "UPDATE DeviceCommands SET Status = 'Delivered', DeliveredAt = {0} WHERE Id = {1} OR Id = {2}",
                nowStr, idUpper, idLower);
        }

        return new AgentQueuedCommand(
            entity.RequestId,
            entity.Kind,
            entity.Shell,
            entity.Command,
            entity.TimeoutSeconds,
            entity.CreatedAt);
    }

    public void MarkDelivered(Guid requestId)
    {
        using var db = _dbFactory.CreateDbContext();
        var entity = db.DeviceCommands.FirstOrDefault(c => c.RequestId == requestId);
        if (entity is null || entity.Status != DeviceCommandStatuses.Queued)
        {
            return;
        }

        entity.Status = DeviceCommandStatuses.Delivered;
        entity.DeliveredAt = DateTimeOffset.UtcNow;
        try
        {
            db.SaveChanges();
        }
        catch (DbUpdateConcurrencyException)
        {
            var idUpper = entity.Id.ToString().ToUpperInvariant();
            var idLower = entity.Id.ToString().ToLowerInvariant();
            var nowStr = DateTimeOffset.UtcNow.ToString("O");
            db.Database.ExecuteSqlRaw(
                "UPDATE DeviceCommands SET Status = 'Delivered', DeliveredAt = {0} WHERE Id = {1} OR Id = {2}",
                nowStr, idUpper, idLower);
        }
    }

    public bool Complete(Guid deviceId, DeviceCommandExecutionResult result)
    {
        using var db = _dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var entity = db.DeviceCommands.FirstOrDefault(c => c.DeviceId == deviceId && c.RequestId == result.RequestId);
        if (entity is null)
        {
            return false;
        }

        if (entity.CompletedAt is not null)
        {
            return entity.ExitCode == result.ExitCode &&
                entity.StdOutPreview == Truncate(result.StdOut, 2000) &&
                entity.StdErrPreview == Truncate(result.StdErr, 2000) &&
                entity.DurationMs == result.DurationMs && entity.TimedOut == result.TimedOut &&
                entity.ElevationDenied == result.ElevationDenied;
        }

        entity.Status = result.TimedOut ? DeviceCommandStatuses.TimedOut : result.ExitCode == 0 ? DeviceCommandStatuses.Completed : DeviceCommandStatuses.Failed;
        entity.CompletedAt = DateTimeOffset.UtcNow;
        entity.ExitCode = result.ExitCode;
        entity.StdOutPreview = Truncate(result.StdOut, 2000);
        entity.StdErrPreview = Truncate(result.StdErr, 2000);
        entity.DurationMs = result.DurationMs;
        entity.TimedOut = result.TimedOut;
        entity.ElevationDenied = result.ElevationDenied;

        if (string.Equals(entity.Kind, "command", StringComparison.OrdinalIgnoreCase))
        {
            db.CommandAudits.Add(new CommandAuditEntity
            {
                Id = Guid.NewGuid(),
                DeviceId = deviceId,
                SessionId = entity.RequestId,
                Shell = entity.Shell,
                Command = entity.Command,
                ExitCode = result.ExitCode,
                StdOutPreview = entity.StdOutPreview ?? string.Empty,
                StdErrPreview = entity.StdErrPreview ?? string.Empty,
                DurationMs = result.DurationMs,
                ExecutedAt = DateTimeOffset.UtcNow,
                InitiatorUserId = entity.InitiatorUserId,
                InitiatorEmail = entity.InitiatorEmail,
                CorrelationId = entity.CorrelationId
            });
        }

        db.SaveChanges();
        transaction.Commit();
        return true;
    }

    public string? GetKind(Guid requestId)
    {
        using var db = _dbFactory.CreateDbContext();
        return db.DeviceCommands
            .AsNoTracking()
            .Where(c => c.RequestId == requestId)
            .Select(c => c.Kind)
            .FirstOrDefault();
    }

    public void MarkTimedOut(Guid requestId, int timeoutSeconds)
    {
        using var db = _dbFactory.CreateDbContext();
        using var transaction = db.Database.BeginTransaction();
        var entity = db.DeviceCommands.FirstOrDefault(c => c.RequestId == requestId);
        if (entity is null || entity.CompletedAt is not null)
        {
            return;
        }

        entity.Status = DeviceCommandStatuses.TimedOut;
        entity.CompletedAt = DateTimeOffset.UtcNow;
        entity.ExitCode = -1;
        entity.StdErrPreview = $"Komut yürütme zaman aşımına uğradı ({timeoutSeconds} sn).";

        if (string.Equals(entity.Kind, "command", StringComparison.OrdinalIgnoreCase))
        {
            db.CommandAudits.Add(new CommandAuditEntity
            {
                Id = Guid.NewGuid(),
                DeviceId = entity.DeviceId,
                SessionId = entity.RequestId,
                Shell = entity.Shell,
                Command = entity.Command,
                ExitCode = -1,
                StdOutPreview = string.Empty,
                StdErrPreview = entity.StdErrPreview ?? string.Empty,
                DurationMs = timeoutSeconds * 1000L,
                ExecutedAt = DateTimeOffset.UtcNow,
                InitiatorUserId = entity.InitiatorUserId,
                InitiatorEmail = entity.InitiatorEmail,
                CorrelationId = entity.CorrelationId
            });
        }
        entity.DurationMs = timeoutSeconds * 1000L;
        entity.TimedOut = true;
        db.SaveChanges();
        transaction.Commit();
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length > max ? value[..max] : value;
    }
}
