using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Endpoints;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Production ortamında varsayılan / eksik credential ile başlatmayı engelle
if (builder.Environment.IsProduction())
{
    var adminPassword = builder.Configuration["Admin:Password"];
    var enrollmentKey = builder.Configuration["Enrollment:Key"];

    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(adminPassword))
        errors.Add("Admin:Password production ortamında ayarlanmalıdır (ilk Admin kullanıcısının bootstrap şifresi).");
    if (string.IsNullOrWhiteSpace(enrollmentKey))
        errors.Add("Enrollment:Key production ortamında ayarlanmalıdır.");

    if (errors.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("=== NEXMOTE BAŞLATMA HATASI: GÜVENLİ KONFİGÜRASYON EKSİK ===");
        foreach (var err in errors) Console.Error.WriteLine($"  ✗ {err}");
        Console.Error.WriteLine("  Sırları /etc/systemd/system/nexmote.service.d/override.conf içinde Environment= satırları olarak tanımlayın.");
        Console.ResetColor();
        Environment.Exit(1);
    }
}

// SQLite veritabanı bağlantısı ve DbContextFactory kaydı
var dbPath = Path.Combine(AppContext.BaseDirectory, "nexmote.db");
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Data Protection anahtarları diske kalıcı yazılır — aksi halde her servis restart'ında
// anahtarlar sıfırlanır ve tüm kullanıcıların MFA secret'ları kalıcı olarak çözülemez hale gelir.
builder.Services.AddDataProtection()
    .SetApplicationName("NexMote")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "dpkeys")));

// Kullanıcı kimlik doğrulama: opak Bearer oturum token'ı (SessionTokenAuthHandler), statik AdminAuthFilter'ın yerini alır
builder.Services.AddAuthentication(SessionTokenAuthHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionTokenAuthHandler>(SessionTokenAuthHandler.SchemeName, _ => { });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("AnyUser", p => p.RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.RequireAuthenticatedUser().RequireRole(UserRoles.Admin));

builder.Services.AddSingleton<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();
builder.Services.AddSingleton<IPasswordHasher<SecurityProfileEntity>, PasswordHasher<SecurityProfileEntity>>();
builder.Services.AddSingleton<TotpService>();
builder.Services.AddSingleton<UserAuthService>();
builder.Services.AddSingleton<EmailService>();
builder.Services.AddSingleton<SecurityProfileService>();
builder.Services.AddSingleton<DeviceGroupService>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<AlertMonitorService>();

// Web ön yüzü ve teknisyen istemcisi için CORS politikası
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173", "http://127.0.0.1:5173", "https://nexmote.com", "https://www.nexmote.com"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Rate limiting: login brute-force koruması (5 deneme/dakika/IP) ve heartbeat DoS koruması
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // /api/auth/login — IP başına 5 deneme/dakika
    options.AddSlidingWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 5;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    // Heartbeat & audit — IP başına 120 istek/dakika (her 20s bir heartbeat = 3/dk; 120 çok sağlıklı bir üst sınır)
    options.AddSlidingWindowLimiter("agent", opt =>
    {
        opt.PermitLimit = 120;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });

    // Genel API — IP başına 300 istek/dakika
    options.AddSlidingWindowLimiter("api", opt =>
    {
        opt.PermitLimit = 300;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 6;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 0;
    });
});

// SignalR yapılandırması (Görüntü ve dosya aktarımı için maksimum 4 MB mesaj boyutu)
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

// Tekil (Singleton) servislerin bağımlılık enjeksiyonuna kaydı
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<RemoteSessionRegistry>();
builder.Services.AddSingleton<SignalSessionAccess>();
builder.Services.AddSingleton<DownloadCatalog>();
builder.Services.AddSingleton<ServerTelemetryService>();
builder.Services.AddSingleton<DeviceCommandManager>();

var app = builder.Build();

// Veritabanı tablolarının oluşturulması ve ilk başlangıç ayarlarının kaydedilmesi
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();
    try
    {
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
    }
    catch { }
    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""DeletedDevices"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeletedDevices"" PRIMARY KEY,
                ""DeviceName"" TEXT NOT NULL,
                ""DomainName"" TEXT NOT NULL,
                ""DeletedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeletedDevices_DeviceName_DomainName"" ON ""DeletedDevices"" (""DeviceName"", ""DomainName"");
        ");
    }
    catch { }

    try
    {
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Devices"" ADD COLUMN ""WindowsUpdatesJson"" TEXT;");
    }
    catch { }

    try
    {
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Devices"" ADD COLUMN ""HardwareDetailsJson"" TEXT;");
    }
    catch { }

    // Çoklu kullanıcı (Admin/Teknisyen), MFA ve denetim logu tabloları — EnsureCreated() zaten var olan bir
    // veritabanı dosyasına yeni tablo eklemediği için (yalnızca sıfırdan oluştururken şemayı uygular),
    // mevcut production/dev veritabanlarına bu tablolar burada elle eklenir (aynı DeletedDevices deseni).
    try
    {
        db.Database.ExecuteSqlRaw(@"
            CREATE TABLE IF NOT EXISTS ""Users"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Users"" PRIMARY KEY,
                ""Email"" TEXT NOT NULL,
                ""DisplayName"" TEXT NOT NULL,
                ""PasswordHash"" TEXT NOT NULL,
                ""Role"" TEXT NOT NULL,
                ""IsActive"" INTEGER NOT NULL,
                ""MfaEnabled"" INTEGER NOT NULL,
                ""MfaSecretEncrypted"" TEXT NULL,
                ""MfaRecoveryCodesHashJson"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""LastLoginAt"" TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_Email"" ON ""Users"" (""Email"");

            CREATE TABLE IF NOT EXISTS ""UserSessions"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_UserSessions"" PRIMARY KEY,
                ""UserId"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""IsMfaPending"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""RevokedAt"" TEXT NULL,
                ""IpAddress"" TEXT NULL,
                ""UserAgent"" TEXT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserSessions_TokenHash"" ON ""UserSessions"" (""TokenHash"");
            CREATE INDEX IF NOT EXISTS ""IX_UserSessions_UserId"" ON ""UserSessions"" (""UserId"");

            CREATE TABLE IF NOT EXISTS ""ActivityLogs"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_ActivityLogs"" PRIMARY KEY,
                ""UserId"" TEXT NULL,
                ""UserEmailSnapshot"" TEXT NULL,
                ""Action"" TEXT NOT NULL,
                ""TargetType"" TEXT NULL,
                ""TargetId"" TEXT NULL,
                ""DetailsJson"" TEXT NULL,
                ""IpAddress"" TEXT NULL,
                ""Success"" INTEGER NOT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_ActivityLogs_UserId"" ON ""ActivityLogs"" (""UserId"");
            CREATE INDEX IF NOT EXISTS ""IX_ActivityLogs_CreatedAt"" ON ""ActivityLogs"" (""CreatedAt"");

            CREATE TABLE IF NOT EXISTS ""UserInvites"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_UserInvites"" PRIMARY KEY,
                ""UserId"" TEXT NOT NULL,
                ""TokenHash"" TEXT NOT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""ExpiresAt"" TEXT NOT NULL,
                ""AcceptedAt"" TEXT NULL,
                ""InvitedByUserId"" TEXT NOT NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""IX_UserInvites_TokenHash"" ON ""UserInvites"" (""TokenHash"");
            CREATE INDEX IF NOT EXISTS ""IX_UserInvites_UserId"" ON ""UserInvites"" (""UserId"");

            CREATE TABLE IF NOT EXISTS ""SecurityProfiles"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_SecurityProfiles"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""AgentDisplayName"" TEXT NULL,
                ""IconBase64"" TEXT NULL,
                ""RestrictTrayMenu"" INTEGER NOT NULL,
                ""RequirePassword"" INTEGER NOT NULL,
                ""PasswordHash"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL,
                ""UpdatedAt"" TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ""DeviceGroups"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeviceGroups"" PRIMARY KEY,
                ""Name"" TEXT NOT NULL,
                ""ParentGroupId"" TEXT NULL,
                ""DefaultSecurityProfileId"" TEXT NULL,
                ""EnrollmentKey"" TEXT NULL,
                ""CreatedAt"" TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeviceGroups_ParentGroupId"" ON ""DeviceGroups"" (""ParentGroupId"");

            CREATE TABLE IF NOT EXISTS ""DeviceAlerts"" (
                ""Id"" TEXT NOT NULL CONSTRAINT ""PK_DeviceAlerts"" PRIMARY KEY,
                ""DeviceId"" TEXT NOT NULL,
                ""AlertType"" TEXT NOT NULL,
                ""TriggeredAt"" TEXT NOT NULL,
                ""LastNotifiedAt"" TEXT NOT NULL,
                ""ResolvedAt"" TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ""IX_DeviceAlerts_DeviceId_ResolvedAt"" ON ""DeviceAlerts"" (""DeviceId"", ""ResolvedAt"");
        ");
    }
    catch { }

    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Devices"" ADD COLUMN ""SecurityProfileId"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Devices"" ADD COLUMN ""GroupId"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""DeviceGroups"" ADD COLUMN ""EnrollmentKey"" TEXT;"); } catch { }

    // Güvenlik profili: eski 3-şifreli şemadan (RequireDashboardPassword vb.) tek şifreye (RequirePassword/
    // PasswordHash) geçiş — eski kolonlar zaten var olan veritabanlarında zararsız şekilde kullanılmadan kalır.
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""RequirePassword"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""PasswordHash"" TEXT;"); } catch { }

    // Bağlantı onayı (consent) ve granüler izin kolonları — aynı ALTER TABLE deseni.
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentMode"" TEXT NOT NULL DEFAULT 'unattended';"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentTimeoutSeconds"" INTEGER NOT NULL DEFAULT 30;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ConsentDefaultAction"" TEXT NOT NULL DEFAULT 'deny';"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ViewOnlyMode"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowRemoteTerminal"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowClipboard"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""AllowFileTransfer"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""ShowConnectionBanner"" INTEGER NOT NULL DEFAULT 1;"); } catch { }

    // SMTP ayar kolonları — ServerSettings tablosu EnsureCreated() ile daha önce oluşturulmuş bir
    // veritabanında bu kolonlar yok, DeletedDevices/Users ile aynı ALTER TABLE deseni.
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpHost"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpPort"" INTEGER NOT NULL DEFAULT 465;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpUsername"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpPasswordEncrypted"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpFromAddress"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""SmtpFromName"" TEXT;"); } catch { }

    // Uyarı/bildirim sistemi ayar kolonları (offline/disk/CPU/RAM eşikleri) — aynı ALTER TABLE deseni.
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertsEnabled"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertRecipientEmails"" TEXT;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertOfflineEnabled"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertOfflineMinutes"" INTEGER NOT NULL DEFAULT 5;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertDiskLowEnabled"" INTEGER NOT NULL DEFAULT 1;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertDiskLowMb"" INTEGER NOT NULL DEFAULT 5000;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertCpuHighEnabled"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertCpuHighPercent"" REAL NOT NULL DEFAULT 90;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertMemoryHighEnabled"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""ServerSettings"" ADD COLUMN ""AlertMemoryHighPercent"" REAL NOT NULL DEFAULT 90;"); } catch { }

    if (!db.ServerSettings.Any())
    {
        var bootstrapUrl = builder.Configuration["PublicUrl"] ?? "https://nexmote.com";
        var bootstrapSetting = new ServerSettingEntity
        {
            ServerUrl = bootstrapUrl,
            EnrollmentKey = builder.Configuration["Enrollment:Key"] ?? "dev-enrollment-key",
            HeartbeatSeconds = 20,
            DefaultLocationCode = "OFFICE",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ServerSettings.Add(bootstrapSetting);
        db.SaveChanges();
    }

    // İlk açılışta Users tablosu boşsa, eski tekil admin konfigürasyonundan (Admin:Email/Password)
    // gerçek bir Admin kullanıcısı seed edilir — production sunucusu koptan geçiş yapmasın diye.
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<UserEntity>>();
    var bootstrapEmail = builder.Configuration["Admin:Email"] ?? "admin@nexmote.com";
    var bootstrapPassword = builder.Configuration["Admin:Password"] ?? "admin123";
    UserAuthService.EnsureBootstrapAdmin(db, passwordHasher, bootstrapEmail, bootstrapPassword);
}

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Statik dosyaların ve React web konsolunun (wwwroot) sunulması
app.UseDefaultFiles();
app.UseStaticFiles();

// Yetkilendirme grupları
var authed = app.MapGroup("/api").RequireAuthorization("AnyUser");
var admin = app.MapGroup("/api").RequireAuthorization("Admin");

// Modüler Endpoint Tanımları
app.MapAuthEndpoints(authed, admin);
app.MapDeviceEndpoints(authed, admin);
admin.MapOrganizationEndpoints();
app.MapSecurityProfileEndpoints(authed, admin);
app.MapSettingsEndpoints(authed, admin);

// SignalR Canlı Hub rotası
app.MapHub<SignalingHub>("/hubs/signaling");

// SPA (Single Page Application) yönlendirmesi - React index.html
app.MapFallbackToFile("index.html");

app.Run();

