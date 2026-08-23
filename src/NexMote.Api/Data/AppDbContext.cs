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

    /// <summary>
    /// Yönetici tarafından web panelinden silinen cihazlar tablosu (otomatik yeniden kaydolmayı engeller).
    /// </summary>
    public DbSet<DeletedDeviceEntity> DeletedDevices => Set<DeletedDeviceEntity>();

    /// <summary>
    /// Web konsoluna ve Teknisyen uygulamasına giriş yapabilen insan kullanıcılar (Admin/Teknisyen) tablosu.
    /// </summary>
    public DbSet<UserEntity> Users => Set<UserEntity>();

    /// <summary>
    /// Kullanıcı oturum token'ları (opak, DB'de sadece hash tutulur) ve bekleyen MFA challenge'ları tablosu.
    /// </summary>
    public DbSet<UserSessionEntity> UserSessions => Set<UserSessionEntity>();

    /// <summary>
    /// İnsan kullanıcıların giriş/çıkış ve yönetimsel eylemlerinin denetim logu tablosu.
    /// </summary>
    public DbSet<ActivityLogEntity> ActivityLogs => Set<ActivityLogEntity>();

    /// <summary>
    /// E-posta ile gönderilen kullanıcı davetleri (kabul edilene kadar geçerli, tek kullanımlık token'lar).
    /// </summary>
    public DbSet<UserInviteEntity> UserInvites => Set<UserInviteEntity>();

    /// <summary>
    /// Kurumsal ajan güvenlik profilleri (branding, kısıtlı tray menüsü, şifre korumaları) tablosu.
    /// </summary>
    public DbSet<SecurityProfileEntity> SecurityProfiles => Set<SecurityProfileEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Silinen cihazlar indeksi
        modelBuilder.Entity<DeletedDeviceEntity>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => new { d.DeviceName, d.DomainName });
        });

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

        // Kullanıcı e-posta benzersizlik indeksi
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
        });

        // Kullanıcı oturumu token hash ve kullanıcı indeksleri
        modelBuilder.Entity<UserSessionEntity>(entity =>
        {
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.TokenHash).IsUnique();
            entity.HasIndex(s => s.UserId);
        });

        // Denetim logu kullanıcı ve zaman indeksleri
        modelBuilder.Entity<ActivityLogEntity>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.HasIndex(a => a.UserId);
            entity.HasIndex(a => a.CreatedAt);
        });

        // Kullanıcı daveti token hash ve kullanıcı indeksleri
        modelBuilder.Entity<UserInviteEntity>(entity =>
        {
            entity.HasKey(i => i.Id);
            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.HasIndex(i => i.UserId);
        });

        // Güvenlik profili birincil anahtar
        modelBuilder.Entity<SecurityProfileEntity>(entity =>
        {
            entity.HasKey(p => p.Id);
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

    /// <summary>Tüm ağ bağdaştırıcıları detayları (JSON).</summary>
    public string? NetworkAdaptersJson { get; set; }

    /// <summary>Cihazda kurulu programların listesi (JSON).</summary>
    public string? InstalledAppsJson { get; set; }

    /// <summary>Cihazda yüklü olan işletim sistemi güncelleştirmeleri (KB / Hotfixes) (JSON).</summary>
    public string? WindowsUpdatesJson { get; set; }

    /// <summary>Cihazın anakart, BIOS, işlemci, RAM modülleri ve fiziksel disklerine ait seri numaraları ve donanım envanter detayları (JSON).</summary>
    public string? HardwareDetailsJson { get; set; }

    /// <summary>Cihaza atanmış kurumsal güvenlik profili (branding/şifre korumaları) — null ise kısıtlama yok.</summary>
    public Guid? SecurityProfileId { get; set; }
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

    /// <summary>SMTP sunucu adresi (örn. smtp.hostinger.com) — davet/test e-postaları için.</summary>
    [MaxLength(256)]
    public string? SmtpHost { get; set; }

    /// <summary>SMTP port (varsayılan 465 — implicit SSL).</summary>
    public int SmtpPort { get; set; } = 465;

    /// <summary>SMTP kullanıcı adı (genelde gönderen e-posta adresiyle aynı).</summary>
    [MaxLength(256)]
    public string? SmtpUsername { get; set; }

    /// <summary>Data Protection ile şifrelenmiş SMTP şifresi — düz metin asla saklanmaz.</summary>
    public string? SmtpPasswordEncrypted { get; set; }

    /// <summary>Giden e-postalarda "Kimden" adresi.</summary>
    [MaxLength(256)]
    public string? SmtpFromAddress { get; set; }

    /// <summary>Giden e-postalarda görünen gönderen adı.</summary>
    [MaxLength(128)]
    public string? SmtpFromName { get; set; }
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

/// <summary>
/// Yönetici tarafından silinen ve otomatik kaydolması engellenen bilgisayar kaydı.
/// </summary>
public sealed class DeletedDeviceEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(256)]
    public string DeviceName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string DomainName { get; set; } = string.Empty;

    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Roller. Admin: tam yetki (kullanıcı yönetimi, ayarlar, cihaz silme, denetim logu).
/// Technician: cihazları görüntüleme, uzaktan oturum açma, komut çalıştırma; yönetimsel ekranlara erişemez.
/// </summary>
public static class UserRoles
{
    public const string Admin = "Admin";
    public const string Technician = "Technician";
}

/// <summary>
/// Web konsoluna ve Teknisyen uygulamasına giriş yapabilen bir insan kullanıcıyı temsil eden veritabanı varlığı.
/// </summary>
public sealed class UserEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Giriş e-postası (benzersiz).</summary>
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Arayüzde gösterilecek görünen ad.</summary>
    [Required]
    [MaxLength(128)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary><see cref="Microsoft.AspNetCore.Identity.PasswordHasher{TUser}"/> ile üretilmiş PBKDF2 şifre hash'i.</summary>
    [Required]
    [MaxLength(512)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary><see cref="UserRoles"/> içindeki rollerden biri.</summary>
    [Required]
    [MaxLength(32)]
    public string Role { get; set; } = UserRoles.Technician;

    /// <summary>false ise hesap devre dışıdır, giriş yapamaz (silinmez — denetim logu bütünlüğü için).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Kullanıcı TOTP tabanlı MFA'yı etkinleştirdi mi.</summary>
    public bool MfaEnabled { get; set; }

    /// <summary>AES ile şifrelenmiş base32 TOTP secret'ı (MFA etkin değilse null).</summary>
    public string? MfaSecretEncrypted { get; set; }

    /// <summary>Tek kullanımlık kurtarma kodlarının hash'lerini içeren JSON dizisi.</summary>
    public string? MfaRecoveryCodesHashJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// Opak kullanıcı oturum token'ı (sadece hash'i DB'de tutulur) ve bekleyen MFA challenge kayıtları.
/// </summary>
public sealed class UserSessionEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Token'ın SHA-256 hash'i — düz metin token asla veritabanına yazılmaz.</summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>true ise bu kayıt henüz MFA doğrulaması bekleyen bir login adım-1 challenge'ıdır, gerçek oturum değildir.</summary>
    public bool IsMfaPending { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Dolu ise oturum manuel (logout) veya idari olarak iptal edilmiştir.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    [MaxLength(256)]
    public string? UserAgent { get; set; }
}

/// <summary>
/// İnsan kullanıcıların giriş/çıkış ve yönetimsel eylemlerinin denetim (audit) kaydı.
/// </summary>
public sealed class ActivityLogEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Eylemi yapan kullanıcı (başarısız login denemesinde null olabilir).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Kullanıcı silinmiş/değişmiş olsa bile logun anlamlı kalması için e-posta anlık görüntüsü.</summary>
    [MaxLength(256)]
    public string? UserEmailSnapshot { get; set; }

    /// <summary>Örn. "login.success", "login.failed", "user.create", "user.role_change", "settings.update".</summary>
    [Required]
    [MaxLength(64)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? TargetType { get; set; }

    [MaxLength(128)]
    public string? TargetId { get; set; }

    /// <summary>Eylemle ilgili ek bağlam (JSON).</summary>
    public string? DetailsJson { get; set; }

    [MaxLength(64)]
    public string? IpAddress { get; set; }

    public bool Success { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// E-posta ile gönderilen bir kullanıcı davetinin (Admin veya Teknisyen) kabul edilene kadar geçerli
/// tek kullanımlık token'ı. Kabul edilince hesabın gerçek şifresi ayarlanır ve davet "kullanılmış" sayılır.
/// </summary>
public sealed class UserInviteEntity
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Davet edilen (rastgele/kullanılamaz şifre hash'iyle önceden oluşturulmuş) kullanıcı.</summary>
    public Guid UserId { get; set; }

    /// <summary>Token'ın SHA-256 hash'i — düz metin token asla veritabanına yazılmaz.</summary>
    [Required]
    [MaxLength(128)]
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Dolu ise davet kabul edilmiş, kullanıcı kendi şifresini belirlemiştir.</summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>Daveti gönderen admin kullanıcının kimliği.</summary>
    public Guid InvitedByUserId { get; set; }
}

/// <summary>
/// Kurumsal ajan güvenlik profili: branding (görünen ad/ikon), kısıtlı tray menüsü ve Durum Paneli/
/// Çıkış/Kaldırma işlemleri için sunucu tarafında doğrulanan şifre korumaları. Cihazlara atanır
/// (<see cref="DeviceEntity.SecurityProfileId"/>); atanmamış cihazlarda hiçbir kısıtlama uygulanmaz.
/// </summary>
public sealed class SecurityProfileEntity
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Boşsa ajan tepsisinde/panelinde varsayılan "NexMote Agent" adı kullanılır.</summary>
    [MaxLength(64)]
    public string? AgentDisplayName { get; set; }

    /// <summary>Küçük bir PNG/ICO'nun base64 kodlaması — tray ikonunda kullanılır (boşsa varsayılan kalkan ikonu).</summary>
    public string? IconBase64 { get; set; }

    /// <summary>true ise tray sağ tık menüsü sadece "Durum Panelini Görüntüle" ve "Çıkış" içerir.</summary>
    public bool RestrictTrayMenu { get; set; }

    public bool RequireDashboardPassword { get; set; }
    public string? DashboardPasswordHash { get; set; }

    public bool RequireExitPassword { get; set; }
    public string? ExitPasswordHash { get; set; }

    public bool RequireUninstallPassword { get; set; }
    public string? UninstallPasswordHash { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
