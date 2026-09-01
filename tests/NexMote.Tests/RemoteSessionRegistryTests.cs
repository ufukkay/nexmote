using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using Xunit;

namespace NexMote.Tests;

public class RemoteSessionRegistryTests
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public RemoteSessionRegistryTests()
    {
        var dbName = $"NexMoteSessionDb_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        _dbFactory = new TestDbContextFactory(options);
    }

    [Fact]
    public void Create_NewSession_ReturnsValidResponseAndLaunchUri()
    {
        var registry = new RemoteSessionRegistry(_dbFactory);
        var deviceId = Guid.NewGuid();

        var sessionResponse = registry.Create(deviceId, "https://nexmote.com");

        Assert.NotEqual(Guid.Empty, sessionResponse.SessionId);
        Assert.Equal(deviceId, sessionResponse.DeviceId);
        Assert.Contains("nexmote://connect", sessionResponse.LaunchUri);

        var retrieved = registry.Get(sessionResponse.SessionId);
        Assert.NotNull(retrieved);
        Assert.Equal(deviceId, retrieved.DeviceId);
    }

    [Fact]
    public void Activate_CorrectToken_ExtendsSessionLifetime()
    {
        var registry = new RemoteSessionRegistry(_dbFactory);
        var deviceId = Guid.NewGuid();
        var sessionResponse = registry.Create(deviceId, "https://nexmote.com");

        var retrieved = registry.Get(sessionResponse.SessionId);
        Assert.NotNull(retrieved);

        var activated = registry.Activate(sessionResponse.SessionId, retrieved.Token);
        var failed = registry.Activate(sessionResponse.SessionId, "wrong-token");

        Assert.NotNull(activated);
        Assert.Null(failed);
    }
}
