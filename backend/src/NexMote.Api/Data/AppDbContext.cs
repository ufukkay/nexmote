using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace NexMote.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();
    public DbSet<RemoteSessionEntity> RemoteSessions => Set<RemoteSessionEntity>();
    public DbSet<ServerSettingEntity> ServerSettings => Set<ServerSettingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => new { d.DeviceName, d.DomainName });
        });

        modelBuilder.Entity<RemoteSessionEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.DeviceId);
        });

        modelBuilder.Entity<ServerSettingEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
        });
    }
}

public sealed class DeviceEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string DeviceName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string DomainName { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? OperatingSystem { get; set; }

    [MaxLength(64)]
    public string? AgentVersion { get; set; }

    [MaxLength(128)]
    public string? SerialNumber { get; set; }

    [MaxLength(64)]
    public string? LocationCode { get; set; }

    [Required]
    [MaxLength(128)]
    public string AgentToken { get; set; } = string.Empty;

    [MaxLength(128)]
    public string? ActiveUser { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public double CpuUsagePercent { get; set; }
    public long MemoryTotalMb { get; set; }
    public long MemoryUsedMb { get; set; }
    public long DiskFreeMb { get; set; }
    public long UptimeSeconds { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset EnrolledAt { get; set; }
}

public sealed class RemoteSessionEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid DeviceId { get; set; }

    [Required]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class ServerSettingEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string ServerUrl { get; set; } = "http://127.0.0.1:5080";

    [Required]
    [MaxLength(128)]
    public string EnrollmentKey { get; set; } = "dev-enrollment-key";

    public int HeartbeatSeconds { get; set; } = 20;

    [MaxLength(64)]
    public string DefaultLocationCode { get; set; } = "OFFICE";

    public DateTimeOffset UpdatedAt { get; set; }
}

