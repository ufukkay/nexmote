using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using Xunit;

namespace NexMote.Tests;

public class DeviceRegistryTests
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public DeviceRegistryTests()
    {
        var dbName = $"NexMoteTestDb_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        _dbFactory = new TestDbContextFactory(options);
    }

    [Fact]
    public void Enroll_NewDevice_RegistersSuccessfully()
    {
        var registry = new DeviceRegistry(_dbFactory);
        var request = new AgentEnrollmentRequest(
            EnrollmentKey: "DEMO-KEY",
            DeviceName: "TEST-PC",
            DomainName: "WORKGROUP",
            OperatingSystem: "Windows 11",
            AgentVersion: "0.7.0",
            SerialNumber: "SN-123",
            LocationCode: "OFFICE");

        var response = registry.Enroll(request);

        Assert.NotEqual(Guid.Empty, response.DeviceId);
        Assert.NotEmpty(response.AgentToken);

        var device = registry.GetById(response.DeviceId);
        Assert.NotNull(device);
        Assert.Equal("TEST-PC", device.DeviceName);
        Assert.Equal("WORKGROUP", device.DomainName);
    }

    [Fact]
    public void Enroll_PreviouslyDeletedDevice_AutomaticallyUnblocksAndEnrolls()
    {
        // 1. Arrange: Cihazı DeletedDevices listesine ekle
        using (var db = _dbFactory.CreateDbContext())
        {
            db.DeletedDevices.Add(new DeletedDeviceEntity
            {
                Id = Guid.NewGuid(),
                DeviceName = "BLOCKED-PC",
                DomainName = "CORP",
                DeletedAt = DateTimeOffset.UtcNow
            });
            db.SaveChanges();
        }

        var registry = new DeviceRegistry(_dbFactory);
        var request = new AgentEnrollmentRequest(
            EnrollmentKey: "DEMO-KEY",
            DeviceName: "BLOCKED-PC",
            DomainName: "CORP",
            OperatingSystem: "Windows 11",
            AgentVersion: "0.7.0",
            SerialNumber: null,
            LocationCode: "HQ");

        // 2. Act: Yeniden kaydol
        var response = registry.Enroll(request);

        // 3. Assert: Engel kalktı ve cihaz başarıyla kaydedildi
        Assert.NotEqual(Guid.Empty, response.DeviceId);

        using (var db = _dbFactory.CreateDbContext())
        {
            var isStillInDeleted = db.DeletedDevices.Any(d => d.DeviceName.ToLower() == "blocked-pc");
            Assert.False(isStillInDeleted);
        }
    }

    [Fact]
    public void ValidateAgent_ValidToken_ReturnsTrue()
    {
        var registry = new DeviceRegistry(_dbFactory);
        var enrolled = registry.Enroll(new AgentEnrollmentRequest(
            EnrollmentKey: "KEY",
            DeviceName: "AUTH-PC",
            DomainName: "WORKGROUP",
            OperatingSystem: "Windows 11",
            AgentVersion: "0.7.0",
            SerialNumber: null,
            LocationCode: "HQ"));

        var isValid = registry.ValidateAgent(enrolled.DeviceId, enrolled.AgentToken);
        var isInvalid = registry.ValidateAgent(enrolled.DeviceId, "wrong-token");

        Assert.True(isValid);
        Assert.False(isInvalid);
    }

    [Fact]
    public void Heartbeat_UpdatesTelemetryAndLastSeenAt()
    {
        var registry = new DeviceRegistry(_dbFactory);
        var enrolled = registry.Enroll(new AgentEnrollmentRequest(
            EnrollmentKey: "KEY",
            DeviceName: "TELEMETRY-PC",
            DomainName: "WORKGROUP",
            OperatingSystem: "Windows 11",
            AgentVersion: "0.7.0",
            SerialNumber: null,
            LocationCode: "HQ"));

        var heartbeat = new DeviceHeartbeatRequest(
            AgentToken: enrolled.AgentToken,
            ActiveUser: "active.user",
            IpAddress: "192.168.1.50",
            CpuUsagePercent: 25,
            MemoryTotalMb: 16384,
            MemoryUsedMb: 8192,
            DiskFreeMb: 250000,
            UptimeSeconds: 3600,
            AgentVersion: "0.7.0");

        var result = registry.Heartbeat(enrolled.DeviceId, heartbeat);

        Assert.True(result);
        var updated = registry.GetById(enrolled.DeviceId);
        Assert.NotNull(updated);
        Assert.Equal("active.user", updated.ActiveUser);
        Assert.Equal(25, updated.CpuUsagePercent);
        Assert.Equal(16384, updated.MemoryTotalMb);
        Assert.Equal(8192, updated.MemoryUsedMb);
    }
}

internal class TestDbContextFactory : IDbContextFactory<AppDbContext>
{
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory(DbContextOptions<AppDbContext> options)
    {
        _options = options;
    }

    public AppDbContext CreateDbContext() => new(_options);
}
