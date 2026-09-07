using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NexMote.Api.Data;
using Xunit;

namespace NexMote.Tests;

public sealed class DatabaseUpgradeTests
{
    [Fact]
    public void LegacyAuditColumnDoesNotPreventCreatingLaterTables()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS IX_ActivityLogs_CorrelationId;");
        db.Database.ExecuteSqlRaw("ALTER TABLE ActivityLogs DROP COLUMN CorrelationId;");
        db.Database.ExecuteSqlRaw("DROP TABLE DeviceCommands;");
        DatabaseInitializer.Initialize(db, NullLogger.Instance);
        Assert.Equal(0, db.DeviceCommands.Count());
        DatabaseInitializer.Initialize(db, NullLogger.Instance);
        Assert.Equal(0, db.ActivityLogs.Count());
    }

    [Fact]
    public void MissingRequiredBaseTableFailsStartup()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw("DROP TABLE Devices;");
        Assert.Throws<SqliteException>(() => DatabaseInitializer.Initialize(db, NullLogger.Instance));
    }
}
