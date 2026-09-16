using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using Xunit;

namespace NexMote.Tests;

public sealed class CommandResultTests
{
    [Fact]
    public void TakeNext_claims_a_command_only_once()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var factory = new Factory(connection);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        var queue = new DeviceCommandQueue(factory);
        var device = Guid.NewGuid();
        queue.Enqueue(Guid.NewGuid(), device, "command", "cmd", "echo test", 30);

        var first = queue.TakeNext(device);
        var second = queue.TakeNext(device);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void TakeNext_requeues_an_uncompleted_expired_delivery()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var factory = new Factory(connection);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        var queue = new DeviceCommandQueue(factory);
        var device = Guid.NewGuid();
        queue.Enqueue(Guid.NewGuid(), device, "command", "cmd", "echo test", 30);

        var first = queue.TakeNext(device);
        Assert.NotNull(first);
        var entity = db.DeviceCommands.Single();
        entity.DeliveredAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        db.SaveChanges();

        var second = queue.TakeNext(device);

        Assert.NotNull(second);
    }

    [Fact]
    public void DuplicateResultCreatesOneAuditAndConflictingResultIsRejected()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var factory = new Factory(connection);
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        var queue = new DeviceCommandQueue(factory);
        var device = Guid.NewGuid();
        var request = Guid.NewGuid();
        queue.Enqueue(request, device, "command", "cmd", "echo test", 30);
        queue.MarkDelivered(request);
        var result = new DeviceCommandExecutionResult(request, 0, "test", "", 10, false, false);
        Assert.True(queue.Complete(device, result));
        Assert.True(queue.Complete(device, result));
        Assert.False(queue.Complete(device, result with { ExitCode = 1 }));
        Assert.False(queue.Complete(Guid.NewGuid(), result));
        Assert.Equal(1, db.CommandAudits.Count());
        Assert.Equal(0, db.DeviceCommands.Single().ExitCode);
    }

    private sealed class Factory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
    }
}
