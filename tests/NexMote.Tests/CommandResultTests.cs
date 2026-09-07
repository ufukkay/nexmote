using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using Xunit;

namespace NexMote.Tests;

public sealed class CommandResultTests
{
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
