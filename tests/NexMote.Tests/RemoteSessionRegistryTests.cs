using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using Xunit;

namespace NexMote.Tests;

public sealed class RemoteSessionRegistryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly RemoteSessionRegistry _registry;

    public RemoteSessionRegistryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        _registry = new RemoteSessionRegistry(new TestDbContextFactory(options));
    }

    [Fact]
    public void Launch_token_is_single_use_and_active_token_is_owner_bound()
    {
        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var response = _registry.Create(Guid.NewGuid(), "https://nexmote.com", ownerId);
        var launchToken = ExtractToken(response.LaunchUri);

        var activated = _registry.Activate(response.SessionId, launchToken, ownerId);

        Assert.NotNull(activated);
        Assert.NotEqual(launchToken, activated!.Token);
        Assert.Null(_registry.Activate(response.SessionId, launchToken, ownerId));
        Assert.Null(_registry.Activate(response.SessionId, activated.Token, otherUserId));
        Assert.NotNull(_registry.Activate(response.SessionId, activated.Token, ownerId));
    }

    [Fact]
    public void Create_with_userToken_includes_it_in_launch_uri()
    {
        var ownerId = Guid.NewGuid();
        var userToken = "test-user-sso-token-abcdef123456";
        var response = _registry.Create(Guid.NewGuid(), "https://nexmote.com", ownerId, userToken);

        Assert.Contains($"userToken={userToken}", response.LaunchUri);
    }

    public void Dispose() => _connection.Dispose();

    private static string ExtractToken(string launchUri)
    {
        var token = launchUri.Split("token=", StringSplitOptions.RemoveEmptyEntries).Last();
        var separatorIndex = token.IndexOf('&');
        return Uri.UnescapeDataString(separatorIndex >= 0 ? token[..separatorIndex] : token);
    }

    private sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext() => new(_options);
    }
}