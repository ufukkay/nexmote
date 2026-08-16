using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace NexMote.Api.Data;

/// <summary>
/// NexMote SQLite veritabanı bağlamı (DbContext).
/// Cihazlar, uzaktan oturumlar, sunucu ayarları ve komut denetim kayıtlarını yönetir.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Kayıtlı istemci cihazlar tablosu.
    /// </summary>
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    /// <summary>
    /// Teknisyen uzaktan kontrol oturumları tablosu.
    /// </summary>
    public DbSet<RemoteSessionEntity> RemoteSessions => Set<RemoteSessionEntity>();

    /// <summary>
    /// Genel sunucu yapılandırma ayarları tablosu (tekil kayıt).
    /// </summary>
    public DbSet<ServerSettingEntity> ServerSettings => Set<ServerSettingEntity>();

    /// <summary>
    /// İstemcilerde çalıştırılan uzak komutların denetim (audit) logları tablosu.
    /// </summary>
    public DbSet<CommandAuditEntity> CommandAudits => Set<CommandAuditEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Cihaz adı ve domain adına göre bileşik indeks
        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => new { d.DeviceName, d.DomainName });
        });

        // Oturum cihaz kimliği indeksi
        modelBuilder.Entity<RemoteSessionEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.DeviceId);
        });

        // Sunucu ayarları birincil anahtar
        modelBuilder.Entity<ServerSettingEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
        });

        // Komut denetimi cihaz ve yürütülme tarihi indeksleri
        modelBuilder.Entity<CommandAuditEntity>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.DeviceId);
            entity.HasIndex(c => c.ExecutedAt);
        });
    }
}

/// <summary>
/// Kayıtlı bir Windows bilgisayarını temsil eden veritabanı varlığı.
/// </summary>
public sealed class DeviceEntity
{
    /// <summary>Cihazın benzersiz Guid kimliği.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Bilgisayar adı (Hostname).</summary>
    [Required]
    [MaxLength(256)]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Domain veya Workgroup adı.</summary>
    [Required]
    [MaxLength(256)]
    public string DomainName { get; set; } = string.Empty;

    /// <summary>İşletim sistemi sürümü.</summary>
    [MaxLength(128)]
    public string? OperatingSystem { get; set; }

    /// <summary>İstemcide çalışan Agent yazılım sürümü.</summary>
    [MaxLength(64)]
    public string? AgentVersion { get; set; }

    /// <summary>Donanım seri numarası (varsa).</summary>
    [MaxLength(128)]
    public string? SerialNumber { get; set; }

    /// <summary>Lokasyon veya departman kodu.</summary>
    [MaxLength(64)]
    public string? LocationCode { get; set; }

    /// <summary>Cihaza özel üretilen 32-byte güvenlik ve sinyal token'ı.</summary>
    [Required]
    [MaxLength(128)]
    public string AgentToken { get; set; } = string.Empty;

    /// <summary>Oturum açmış kullanıcı veya makine hesabı.</summary>
    [MaxLength(128)]
    public string? ActiveUser { get; set; }

    /// <summary>Cihazın yerel fiziksel IPv4 adresi.</summary>
    [MaxLength(64)]
    public string? IpAddress { get; set; }

    /// <summary>GetSystemTimes ile ölçülen 10 dakikalık kayan pencere ortalama CPU kullanım yüzdesi.</summary>
    public double CpuUsagePercent { get; set; }

    /// <summary>Toplam fiziksel RAM (MB).</summary>
    public long MemoryTotalMb { get; set; }

    /// <summary>Kullanılan fiziksel RAM (MB).</summary>
    public long MemoryUsedMb { get; set; }

    /// <summary>Sistem diskindeki (C:\) boş alan (MB).</summary>
    public long DiskFreeMb { get; set; }

    /// <summary>Sistemin açık kalma süresi (Saniye).</summary>
    public long UptimeSeconds { get; set; }

    /// <summary>Son heartbeat veya sinyal alınma zamanı.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Cihazın ilk kayıt (enrollment) zamanı.</summary>
    public DateTimeOffset EnrolledAt { get; set; }
}

/// <summary>
/// Teknisyen için üretilen geçici canlı bağlantı oturumunu temsil eden veritabanı varlığı.
/// </summary>
public sealed class RemoteSessionEntity
{
    /// <summary>Oturum benzersiz kimliği.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Bağlanılan hedef cihaz kimliği.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Oturuma özel 24-byte güvenlik doğrulama token'ı.</summary>
    [Required]
    [MaxLength(128)]
    public string Token { get; set; } = string.Empty;

    /// <summary>Oturum oluşturulma zamanı.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Oturum token geçerlilik bitiş zamanı (varsayılan: 5 dakika).</summary>
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// Sunucunun çalışma zamanı yapılandırma ayarlarını saklayan veritabanı varlığı.
/// </summary>
public sealed class ServerSettingEntity
{
    /// <summary>Ayar ID (Tekil kayıt: 1).</summary>
    [Key]
    public int Id { get; set; }

    /// <summary>Sunucu ana bağlantı URL'i.</summary>
    [Required]
    [MaxLength(256)]
    public string ServerUrl { get; set; } = "http://127.0.0.1:5080";

    /// <summary>İstemcilerin ilk kayıt esnasında gönderdiği ortak kayıt anahtarı.</summary>
    [Required]
    [MaxLength(128)]
    public string EnrollmentKey { get; set; } = "dev-enrollment-key";

    /// <summary>Heartbeat gönderim periyodu (varsayılan: 20 saniye).</summary>
    public int HeartbeatSeconds { get; set; } = 20;

    /// <summary>Varsayılan lokasyon kodu.</summary>
    [MaxLength(64)]
    public string DefaultLocationCode { get; set; } = "OFFICE";

    /// <summary>Son ayar güncelleme zamanı.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// İstemcide çalıştırılan CMD/PowerShell komutlarının denetim kaydını tutan veritabanı varlığı.
/// </summary>
public sealed class CommandAuditEntity
{
    /// <summary>Denetim kaydı benzersiz kimliği.</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Komutun çalıştırıldığı cihaz kimliği.</summary>
    public Guid DeviceId { get; set; }

    /// <summary>Komutun yürütüldüğü oturum kimliği.</summary>
    public Guid SessionId { get; set; }

    /// <summary>Kullanılan kabuk türü ("cmd" veya "powershell").</summary>
    [Required]
    [MaxLength(32)]
    public string Shell { get; set; } = string.Empty;

    /// <summary>Çalıştırılan komut metni.</summary>
    [Required]
    [MaxLength(4000)]
    public string Command { get; set; } = string.Empty;

    /// <summary>Komut işlem çıkış kodu (0 = Başarılı).</summary>
    public int ExitCode { get; set; }

    /// <summary>Standart çıktı özeti.</summary>
    [MaxLength(2000)]
    public string StdOutPreview { get; set; } = string.Empty;

    /// <summary>Hata çıktısı özeti.</summary>
    [MaxLength(2000)]
    public string StdErrPreview { get; set; } = string.Empty;

    /// <summary>Komutun yürütülme süresi (milisaniye).</summary>
    public long DurationMs { get; set; }

    /// <summary>Komutun yürütüldüğü zaman damgası.</summary>
    public DateTimeOffset ExecutedAt { get; set; }
}
