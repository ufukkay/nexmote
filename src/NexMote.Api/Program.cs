using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
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

    // Güvenlik profili: eski 3-şifreli şemadan (RequireDashboardPassword vb.) tek şifreye (RequirePassword/
    // PasswordHash) geçiş — eski kolonlar zaten var olan veritabanlarında zararsız şekilde kullanılmadan kalır.
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""RequirePassword"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""SecurityProfiles"" ADD COLUMN ""PasswordHash"" TEXT;"); } catch { }

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

/// <summary>
/// Sunucu sağlık kontrol endpoint'i.
/// </summary>
app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

/// <summary>
/// Giriş adım 1: e-posta/şifre doğrular. MFA kapalıysa doğrudan oturum token'ı, açıksa bir MFA challenge token'ı döner.
/// </summary>
app.MapPost("/api/auth/login", (AdminLoginRequest request, bool? rememberMe, HttpContext http, UserAuthService auth) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var response = auth.LoginStep1(request.Email, request.Password, rememberMe ?? false, ip, http.Request.Headers.UserAgent.ToString());
    return response is null ? Results.Unauthorized() : Results.Ok(response);
}).RequireRateLimiting("login");

/// <summary>
/// Giriş adım 2: MFA challenge token'ı + authenticator uygulamasından okunan 6 haneli kod (veya kurtarma kodu).
/// </summary>
app.MapPost("/api/auth/mfa/verify", (MfaVerifyRequest request, bool? rememberMe, HttpContext http, UserAuthService auth) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var response = auth.VerifyMfaStep2(request.ChallengeToken, request.Code, rememberMe ?? false, ip, http.Request.Headers.UserAgent.ToString());
    return response is null ? Results.Unauthorized() : Results.Ok(response);
}).RequireRateLimiting("login");

/// <summary>Davet önizlemesi (e-posta/rol) — davet kabul ekranı bu endpoint'i kullanır (public).</summary>
app.MapGet("/api/invite/{token}", (string token, UserAuthService auth) =>
{
    var preview = auth.GetInvitePreview(token);
    return preview is null
        ? Results.NotFound(new { message = "Davet geçersiz, süresi dolmuş veya zaten kullanılmış." })
        : Results.Ok(new InvitePreviewResponse(preview.Value.Email, preview.Value.DisplayName, preview.Value.Role));
}).RequireRateLimiting("login");

/// <summary>Daveti kabul eder: şifre belirlenir, hesap etkinleşir, otomatik oturum açılır (public).</summary>
app.MapPost("/api/invite/{token}/accept", (string token, AcceptInviteRequest request, HttpContext http, UserAuthService auth) =>
{
    var ip = http.Connection.RemoteIpAddress?.ToString();
    var response = auth.AcceptInvite(token, request.Password, ip, http.Request.Headers.UserAgent.ToString());
    return response is null
        ? Results.BadRequest(new { message = "Davet geçersiz, süresi dolmuş veya zaten kullanılmış." })
        : Results.Ok(response);
}).RequireRateLimiting("login");

/// <summary>
/// Mevcut oturumu (Bearer token'ı) iptal eder.
/// </summary>
app.MapPost("/api/auth/logout", (HttpContext http, UserAuthService auth) =>
{
    var header = http.Request.Headers.Authorization.ToString();
    if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        auth.Logout(header["Bearer ".Length..].Trim(), http.Connection.RemoteIpAddress?.ToString());
    }
    return Results.NoContent();
}).RequireAuthorization("AnyUser");

/// <summary>
/// Giriş yapmış kullanıcının kimlik/rol bilgisini döner — web/Teknisyen UI'ının rol bazlı render yapması için.
/// </summary>
app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
{
    return Results.Ok(new CurrentUserResponse(
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
        user.FindFirstValue(ClaimTypes.Email)!,
        user.FindFirstValue("display_name") ?? user.FindFirstValue(ClaimTypes.Email)!,
        user.FindFirstValue(ClaimTypes.Role)!,
        MfaEnabled: user.FindFirstValue("mfa_enabled") == "true"));
}).RequireAuthorization("AnyUser");

// Herkes (Admin + Teknisyen) giriş yapmış olmalı — cihaz görüntüleme, uzak oturum, komut çalıştırma
var authed = app.MapGroup("/api").RequireAuthorization("AnyUser");

// Sadece Admin — kullanıcı yönetimi, sunucu ayarları, cihaz silme, denetim logu
var admin = app.MapGroup("/api").RequireAuthorization("Admin");

/// <summary>Kendi şifresini değiştirme.</summary>
authed.MapPost("/account/password", (ChangePasswordRequest request, ClaimsPrincipal user, UserAuthService auth) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return auth.ChangePassword(userId, request.CurrentPassword, request.NewPassword)
        ? Results.NoContent()
        : Results.BadRequest(new { message = "Mevcut şifre hatalı." });
});

/// <summary>MFA kurulumu başlatır — QR (otpauth:// URI) ve secret döner.</summary>
authed.MapPost("/account/mfa/setup", (ClaimsPrincipal user, UserAuthService auth) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return Results.Ok(auth.SetupMfa(userId));
});

/// <summary>MFA kurulumunu ilk 6 haneli kodla onaylar, kurtarma kodlarını bir kereliğine döner.</summary>
authed.MapPost("/account/mfa/enable", (MfaEnableRequest request, ClaimsPrincipal user, UserAuthService auth) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = auth.EnableMfa(userId, request.Code);
    return result is null ? Results.BadRequest(new { message = "Kod doğrulanamadı." }) : Results.Ok(result);
});

/// <summary>MFA'yı kapatır (mevcut şifre doğrulaması gerektirir).</summary>
authed.MapPost("/account/mfa/disable", (MfaDisableRequest request, ClaimsPrincipal user, UserAuthService auth) =>
{
    var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return auth.DisableMfa(userId, request.CurrentPassword)
        ? Results.NoContent()
        : Results.BadRequest(new { message = "Şifre hatalı." });
});

/// <summary>Kullanıcı listesi (Admin Yetkisi Gerekir).</summary>
admin.MapGet("/admin/users", (UserAuthService auth) => Results.Ok(auth.ListUsers()));

/// <summary>Yeni Admin veya Teknisyen hesabı oluşturur, tek seferlik geçici şifre döner (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users", (CreateUserRequest request, ClaimsPrincipal actor, UserAuthService auth) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = auth.CreateUser(request.Email, request.DisplayName, request.Role, actingUserId);
    return result is null ? Results.BadRequest(new { message = "E-posta zaten kayıtlı veya rol geçersiz." }) : Results.Ok(result);
});

/// <summary>Yeni bir kullanıcıyı e-posta ile davet eder — geçici şifre göstermek yerine bir davet linki gönderir (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users/invite", async (InviteUserRequest request, ClaimsPrincipal actor, UserAuthService auth, EmailService email, IConfiguration config) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = auth.InviteUser(request.Email, request.DisplayName, request.Role, actingUserId);
    if (result is null)
    {
        return Results.BadRequest(new { message = "E-posta zaten kayıtlı (ve daveti kabul edilmiş) ya da rol geçersiz." });
    }

    var baseUrl = config["PublicUrl"] ?? "https://nexmote.com";
    var inviteUrl = $"{baseUrl.TrimEnd('/')}/invite/{result.Value.Token}";
    var roleLabel = result.Value.Role == UserRoles.Admin ? "Admin" : "Teknisyen";
    var (success, error) = await email.SendAsync(
        result.Value.Email,
        "NexMote'a Davet Edildiniz",
        $"""
        <p>Merhaba {System.Net.WebUtility.HtmlEncode(result.Value.DisplayName)},</p>
        <p>NexMote uzaktan yönetim konsoluna <strong>{roleLabel}</strong> yetkisiyle davet edildiniz.</p>
        <p>Hesabınızı etkinleştirmek ve kendi şifrenizi belirlemek için aşağıdaki bağlantıya tıklayın (7 gün geçerlidir):</p>
        <p><a href="{inviteUrl}">{inviteUrl}</a></p>
        """);

    if (!success)
    {
        return Results.BadRequest(new { message = error });
    }

    return Results.Ok(new { message = "Davet e-postası gönderildi.", email = result.Value.Email });
});

/// <summary>Kullanıcının rolünü değiştirir (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users/{id:guid}/role", (Guid id, SetRoleRequest request, ClaimsPrincipal actor, UserAuthService auth) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return auth.SetRole(id, request.Role, actingUserId) ? Results.NoContent() : Results.BadRequest(new { message = "Rol geçersiz veya kullanıcı bulunamadı." });
});

/// <summary>Kullanıcı hesabını devre dışı bırakır — tüm aktif oturumları anında iptal eder (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users/{id:guid}/disable", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    if (id == actingUserId)
    {
        return Results.BadRequest(new { message = "Kendi hesabınızı devre dışı bırakamazsınız." });
    }
    return auth.SetActive(id, false, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Devre dışı bırakılmış kullanıcı hesabını yeniden etkinleştirir (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users/{id:guid}/enable", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return auth.SetActive(id, true, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Kilitlenmiş bir kullanıcının MFA'sını admin zorla kapatır (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/users/{id:guid}/mfa/reset", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return auth.AdminResetMfa(id, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Sayfalanmış, filtrelenebilir denetim (activity) logu (Admin Yetkisi Gerekir).</summary>
admin.MapGet("/admin/audit-log", (int? page, int? pageSize, Guid? userId, string? action, UserAuthService auth) =>
{
    var (items, total) = auth.GetAuditLog(page ?? 1, pageSize ?? 50, userId, action);
    return Results.Ok(new { items, total });
});

/// <summary>Güvenlik profillerini listeler (Admin Yetkisi Gerekir).</summary>
admin.MapGet("/admin/security-profiles", (SecurityProfileService profiles) => Results.Ok(profiles.List()));

/// <summary>Yeni güvenlik profili oluşturur (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/security-profiles", (SecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = profiles.Create(request, actingUserId);
    return result is null ? Results.BadRequest(new { message = "Ad boş olamaz veya zorunlu bir şifre eksik." }) : Results.Ok(result);
});

/// <summary>Güvenlik profilini günceller (Admin Yetkisi Gerekir).</summary>
admin.MapPut("/admin/security-profiles/{id:guid}", (Guid id, SecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = profiles.Update(id, request, actingUserId);
    return result is null ? Results.BadRequest(new { message = "Profil bulunamadı, ad boş olamaz veya zorunlu bir şifre eksik." }) : Results.Ok(result);
});

/// <summary>Güvenlik profilini siler — atanmış cihazlar önce kısıtlamasız hale getirilir (Admin Yetkisi Gerekir).</summary>
admin.MapDelete("/admin/security-profiles/{id:guid}", (Guid id, ClaimsPrincipal actor, SecurityProfileService profiles) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return profiles.Delete(id, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Bir cihaza güvenlik profili atar/kaldırır (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/devices/{id:guid}/security-profile", (Guid id, AssignSecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return profiles.AssignToDevice(id, request.SecurityProfileId, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Cihaz gruplarını (şirket/departman) listeler (Admin Yetkisi Gerekir).</summary>
admin.MapGet("/admin/device-groups", (DeviceGroupService groups) => Results.Ok(groups.List()));

/// <summary>Yeni cihaz grubu oluşturur (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/device-groups", (DeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = groups.Create(request, actingUserId);
    return result is null ? Results.BadRequest(new { message = "Ad boş olamaz veya üst grup/profil geçersiz." }) : Results.Ok(result);
});

/// <summary>Cihaz grubunu günceller (Admin Yetkisi Gerekir).</summary>
admin.MapPut("/admin/device-groups/{id:guid}", (Guid id, DeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var result = groups.Update(id, request, actingUserId);
    return result is null ? Results.BadRequest(new { message = "Grup bulunamadı, ad boş olamaz, üst grup bir çevrim oluşturuyor ya da profil geçersiz." }) : Results.Ok(result);
});

/// <summary>Cihaz grubunu siler — alt grubu varsa reddedilir, atanmış cihazlar serbest kalır (Admin Yetkisi Gerekir).</summary>
admin.MapDelete("/admin/device-groups/{id:guid}", (Guid id, ClaimsPrincipal actor, DeviceGroupService groups) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    var (success, error) = groups.Delete(id, actingUserId);
    return success ? Results.NoContent() : Results.BadRequest(new { message = error });
});

/// <summary>Bir cihazı bir gruba atar/kaldırır (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/devices/{id:guid}/group", (Guid id, AssignDeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups) =>
{
    var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
    return groups.AssignDeviceToGroup(id, request.GroupId, actingUserId) ? Results.NoContent() : Results.NotFound();
});

/// <summary>Şu an açık (çözülmemiş) tüm cihaz uyarılarını listeler (Admin veya Teknisyen girişi gerekir — eşik/alıcı ayarları hâlâ Admin-only /api/settings üzerinden yönetilir).</summary>
authed.MapGet("/alerts/active", (AlertService alerts) => Results.Ok(alerts.ListActive()));

/// <summary>
/// Yeni Windows Agent istemcisinin sunucuya ilk kaydı (Enrollment).
/// </summary>
app.MapPost("/api/agents/enroll", (AgentEnrollmentRequest request, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory, IConfiguration config) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.AsNoTracking().FirstOrDefault();
    var expectedKey = setting?.EnrollmentKey ?? config["Enrollment:Key"] ?? "dev-enrollment-key";

    var isAuthorized = !string.IsNullOrEmpty(request.EnrollmentKey) &&
                       string.Equals(request.EnrollmentKey, expectedKey, StringComparison.Ordinal);

    if (!isAuthorized)
    {
        return Results.Unauthorized();
    }

    try
    {
        var enrolled = devices.Enroll(request);
        return Results.Ok(enrolled);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new { message = ex.Message }, statusCode: 403);
    }
});

/// <summary>
/// İstemcinin periyodik 20s canlılık bildirimi ve donanım telemetrisi (CPU, RAM, Disk, Uptime).
/// </summary>
app.MapPost("/api/agents/{deviceId:guid}/heartbeat", (Guid deviceId, DeviceHeartbeatRequest request, DeviceRegistry devices) =>
{
    return devices.Heartbeat(deviceId, request)
        ? Results.NoContent()
        : Results.NotFound(new { message = "Cihaz bulunamadı veya güvenlik token'ı geçersiz." });
}).RequireRateLimiting("agent");

/// <summary>
/// Ajanın (Tray/Cleaner) kendi güvenlik profilini (branding + hangi işlemlerin şifre istediği) sorgulaması.
/// AgentToken ile korunur, insan auth'una girmez; şifre hash'i kesinlikle dönmez.
/// </summary>
app.MapGet("/api/agents/{deviceId:guid}/security-profile", (Guid deviceId, string agentToken, SecurityProfileService profiles) =>
{
    var result = profiles.GetAgentProfile(deviceId, agentToken);
    return result is null ? Results.Unauthorized() : Results.Ok(result);
}).RequireRateLimiting("agent");

/// <summary>
/// Ajanın, kullanıcının girdiği Durum Paneli/Çıkış/Kaldırma şifresini sunucuda doğrulatması. AgentToken ile korunur.
/// </summary>
app.MapPost("/api/agents/{deviceId:guid}/security/verify", (Guid deviceId, SecurityVerifyRequest request, SecurityProfileService profiles) =>
{
    var ok = profiles.VerifyPassword(deviceId, request.AgentToken, request.Action, request.Password);
    return Results.Ok(new SecurityVerifyResponse(ok));
}).RequireRateLimiting("agent");

/// <summary>
/// Kayıtlı tüm cihazların ve donanım metriklerinin listesi (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapGet("/devices", (DeviceRegistry devices) => Results.Ok(devices.List()));

/// <summary>
/// Kayıtlı cihazı sistemden kalıcı olarak silme endpoint'i (Admin Yetkisi Gerekir).
/// uninstallAgent parametresi true ise hedef bilgisayardaki ajanı da sessizce kaldırır.
/// </summary>
admin.MapDelete("/devices/{id:guid}", async (Guid id, bool? uninstallAgent, DeviceRegistry devices, IHubContext<SignalingHub> hub) =>
{
    if (uninstallAgent ?? true)
    {
        try
        {
            await hub.Clients.Group($"device:{id}").SendAsync("RemoteUninstallRequested");
        }
        catch { }
    }

    var deleted = devices.Delete(id);
    return deleted ? Results.NoContent() : Results.NotFound(new { message = "Cihaz bulunamadı." });
});

/// <summary>
/// İndirilebilir kurulum paketlerinin listesi.
/// </summary>
app.MapGet("/api/downloads", (DownloadCatalog downloads) => Results.Ok(downloads.List()));

/// <summary>
/// Sunucu genel ayarlarını okuma (Admin Yetkisi Gerekir).
/// </summary>
admin.MapGet("/settings", (IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.AsNoTracking().First();
    return Results.Ok(new ServerSettingsContract(
        setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode,
        SmtpHost: setting.SmtpHost, SmtpPort: setting.SmtpPort, SmtpUsername: setting.SmtpUsername,
        SmtpPassword: null, SmtpFromAddress: setting.SmtpFromAddress, SmtpFromName: setting.SmtpFromName,
        AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
        AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
        AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
        AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
        AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
});

/// <summary>
/// Sunucu genel ayarlarını güncelleme (Admin Yetkisi Gerekir). SmtpPassword boş bırakılırsa mevcut
/// (şifreli) SMTP şifresi korunur — GET yanıtı asla düz metin şifreyi içermediği için "değiştirme" varsayılan davranıştır.
/// </summary>
admin.MapPost("/settings", (ServerSettingsContract request, IDbContextFactory<AppDbContext> dbFactory, EmailService email) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.First();
    setting.ServerUrl = request.ServerUrl.TrimEnd('/');
    setting.EnrollmentKey = request.EnrollmentKey;
    setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
    setting.DefaultLocationCode = request.DefaultLocationCode;
    setting.SmtpHost = request.SmtpHost;
    setting.SmtpPort = request.SmtpPort <= 0 ? 465 : request.SmtpPort;
    setting.SmtpUsername = request.SmtpUsername;
    setting.SmtpFromAddress = request.SmtpFromAddress;
    setting.SmtpFromName = request.SmtpFromName;
    if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
    {
        setting.SmtpPasswordEncrypted = email.EncryptPassword(request.SmtpPassword);
    }
    setting.AlertsEnabled = request.AlertsEnabled;
    setting.AlertRecipientEmails = request.AlertRecipientEmails;
    setting.AlertOfflineEnabled = request.AlertOfflineEnabled;
    setting.AlertOfflineMinutes = Math.Max(1, request.AlertOfflineMinutes);
    setting.AlertDiskLowEnabled = request.AlertDiskLowEnabled;
    setting.AlertDiskLowMb = Math.Max(0, request.AlertDiskLowMb);
    setting.AlertCpuHighEnabled = request.AlertCpuHighEnabled;
    setting.AlertCpuHighPercent = request.AlertCpuHighPercent;
    setting.AlertMemoryHighEnabled = request.AlertMemoryHighEnabled;
    setting.AlertMemoryHighPercent = request.AlertMemoryHighPercent;
    setting.UpdatedAt = DateTimeOffset.UtcNow;

    db.SaveChanges();
    return Results.Ok(new ServerSettingsContract(
        setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode,
        SmtpHost: setting.SmtpHost, SmtpPort: setting.SmtpPort, SmtpUsername: setting.SmtpUsername,
        SmtpPassword: null, SmtpFromAddress: setting.SmtpFromAddress, SmtpFromName: setting.SmtpFromName,
        AlertsEnabled: setting.AlertsEnabled, AlertRecipientEmails: setting.AlertRecipientEmails,
        AlertOfflineEnabled: setting.AlertOfflineEnabled, AlertOfflineMinutes: setting.AlertOfflineMinutes,
        AlertDiskLowEnabled: setting.AlertDiskLowEnabled, AlertDiskLowMb: setting.AlertDiskLowMb,
        AlertCpuHighEnabled: setting.AlertCpuHighEnabled, AlertCpuHighPercent: setting.AlertCpuHighPercent,
        AlertMemoryHighEnabled: setting.AlertMemoryHighEnabled, AlertMemoryHighPercent: setting.AlertMemoryHighPercent));
});

/// <summary>SMTP ayarlarını (kayıtlı olan) test etmek için verilen adrese bir test e-postası gönderir (Admin Yetkisi Gerekir).</summary>
admin.MapPost("/admin/settings/smtp/test", async (SmtpTestRequest request, EmailService email) =>
{
    var (success, error) = await email.SendAsync(
        request.ToEmail,
        "NexMote - Test E-postası",
        "<p>Bu, NexMote sunucunuzun SMTP yapılandırmasını doğrulamak için gönderilen bir test e-postasıdır.</p>");
    return success ? Results.Ok(new { message = "Test e-postası gönderildi." }) : Results.BadRequest(new { message = error });
});

/// <summary>
/// Sunucu anlık performans ve donanım metriklerini (CPU, RAM, Disk, Anlık Ağ Bant Genişliği Mbps) getirme.
/// Admin veya Teknisyen girişi gerekir.
/// </summary>
authed.MapGet("/server-metrics", (ServerTelemetryService metrics) => Results.Ok(metrics.GetMetrics()));

/// <summary>
/// MSI veya kurulum dosyasını doğrudan indirme endpoint'i.
/// </summary>
app.MapGet("/downloads/{fileName}", (string fileName, DownloadCatalog downloads) =>
{
    var file = downloads.GetFile(fileName);
    return file is null
        ? Results.NotFound(new { message = "İndirme paketi bulunamadı." })
        : Results.File(file.Path, file.ContentType, file.FileName);
});

/// <summary>
/// En son Agent ve Teknisyen sürümlerini ve güncelleme indirme linklerini dönen OTA kontrol endpoint'i.
/// </summary>
app.MapGet("/api/updates/check", (IConfiguration config, DownloadCatalog downloads) =>
{
    var baseUrl = config["PublicUrl"] ?? "https://nexmote.com";
    var versions = downloads.GetVersionInfo();
    return Results.Ok(new
    {
        agent = new
        {
            version = versions.Agent.Version,
            downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Agent-Setup.msi",
            releaseNotes = versions.Agent.ReleaseNotes
        },
        technician = new
        {
            version = versions.Technician.Version,
            downloadUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Technician-Setup.msi",
            releaseNotes = versions.Technician.ReleaseNotes
        }
    });
});

app.MapGet("/api/network-test/download", (int? sizeKb) =>
{
    var bytes = Math.Clamp((sizeKb ?? 1024) * 1024, 64 * 1024, 4 * 1024 * 1024);
    var payload = CreateNetworkTestPayload(bytes);
    return Results.File(payload, "application/octet-stream", enableRangeProcessing: false);
});

app.MapPost("/api/network-test/upload", async (HttpRequest request) =>
{
    const int maxBytes = 1024 * 1024;
    var total = await DrainWithLimitAsync(request.Body, maxBytes);
    return Results.Ok(new { bytes = total, at = DateTimeOffset.UtcNow });
});

app.MapGet("/api/agents/{deviceId:guid}/network-test/download", (Guid deviceId, string agentToken, int? sizeKb, DeviceRegistry devices) =>
{
    if (!devices.ValidateAgent(deviceId, agentToken))
    {
        return Results.Unauthorized();
    }

    var bytes = Math.Clamp((sizeKb ?? 1024) * 1024, 64 * 1024, 4 * 1024 * 1024);
    return Results.File(CreateNetworkTestPayload(bytes), "application/octet-stream", enableRangeProcessing: false);
});

app.MapPost("/api/agents/{deviceId:guid}/network-test/upload", async (Guid deviceId, string agentToken, HttpRequest request, DeviceRegistry devices) =>
{
    if (!devices.ValidateAgent(deviceId, agentToken))
    {
        return Results.Unauthorized();
    }

    const int maxBytes = 1024 * 1024;
    var total = await DrainWithLimitAsync(request.Body, maxBytes);
    return Results.Ok(new { bytes = total, at = DateTimeOffset.UtcNow });
});

/// <summary>
/// Seçili çevrimiçi cihaza uzaktan sessiz Agent MSI güncelleme sinyali gönderme (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapPost("/agents/{deviceId:guid}/update", async (Guid deviceId, Microsoft.AspNetCore.SignalR.IHubContext<SignalingHub> hub, DeviceRegistry devices, IConfiguration config) =>
{
    var device = devices.Get(deviceId);
    if (device is null)
    {
        return Results.NotFound(new { message = "Cihaz bulunamadı." });
    }

    if (!device.IsOnline)
    {
        return Results.BadRequest(new { message = "Cihaz çevrimdışı." });
    }

    var baseUrl = config["PublicUrl"] ?? "https://nexmote.com";
    var msiUrl = $"{baseUrl.TrimEnd('/')}/downloads/NexMote-Agent-Setup.msi";

    await hub.Clients.Group($"device:{deviceId}").SendAsync("RemoteUpdateRequested", msiUrl);
    return Results.Ok(new { message = "Sessiz Agent güncelleme sinyali cihaza başarıyla iletildi." });
});

/// <summary>
/// Tekil cihaz detayını getirme (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapGet("/devices/{deviceId:guid}", (Guid deviceId, DeviceRegistry devices) =>
{
    var device = devices.Get(deviceId);
    return device is null ? Results.NotFound() : Results.Ok(device);
});

/// <summary>
/// Teknisyen için nexmote:// deep-link bağlantı oturumu oluşturma (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapPost("/remote-sessions", (CreateRemoteSessionRequest request, HttpContext http, DeviceRegistry devices, RemoteSessionRegistry sessions, IConfiguration config) =>
{
    var device = devices.Get(request.DeviceId);
    if (device is null)
    {
        return Results.NotFound(new { message = "Cihaz bulunamadı." });
    }

    if (!device.IsOnline)
    {
        return Results.BadRequest(new { message = "Cihaz çevrimdışı." });
    }

    var serverUrl = config["PublicUrl"];
    if (string.IsNullOrWhiteSpace(serverUrl))
    {
        serverUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    }

    return Results.Ok(sessions.Create(request.DeviceId, serverUrl));
});

/// <summary>
/// Web Konsolundan doğrudan cihaz üzerinde CMD veya PowerShell komutu çalıştırma (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapPost("/devices/{id:guid}/execute-command", async (
    Guid id,
    ExecuteCommandApiRequest request,
    Microsoft.AspNetCore.SignalR.IHubContext<SignalingHub> hubContext,
    DeviceCommandManager commandManager,
    DeviceRegistry deviceRegistry,
    CancellationToken ct) =>
{
    var device = deviceRegistry.GetById(id);
    if (device is null)
    {
        return Results.NotFound(new { message = "Cihaz bulunamadı." });
    }

    if (!device.IsOnline)
    {
        return Results.BadRequest(new { message = "Cihaz çevrimdışı, komut gönderilemez." });
    }

    if (string.IsNullOrWhiteSpace(request.Command))
    {
        return Results.BadRequest(new { message = "Komut boş olamaz." });
    }

    var requestId = Guid.NewGuid();
    var tcs = commandManager.RegisterCommand(requestId);

    var shell = string.Equals(request.Shell, "cmd", StringComparison.OrdinalIgnoreCase) ? "cmd" : "powershell";
    var command = request.Command.Trim();

    // Hedef cihazın kalıcı SignalR dinleme grubuna komut fırlat.
    // runAsAdmin: false olarak iletilir — Windows Servisi zaten NT AUTHORITY\SYSTEM (tam yönetici) olarak çalışır;
    // böylece kullanıcı masaüstünde UAC / "Kullanıcı Hesabı Denetimi" pencereleri asla çıkmaz.
    await hubContext.Clients.Group($"device:{id}").SendAsync(
        "ExecuteWebCommand", requestId, shell, command, false, ct);

    // Yanıtı zaman aşımı süresine kadar bekle (varsayılan 30 sn, min 5 sn, maks 120 sn)
    var timeoutSec = Math.Clamp(request.TimeoutSeconds ?? 30, 5, 120);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

    try
    {
        using (cts.Token.Register(() => commandManager.CancelCommand(requestId)))
        {
            var result = await tcs.Task;
            return Results.Ok(new
            {
                requestId = result.RequestId,
                shell,
                command,
                exitCode = result.ExitCode,
                stdOut = result.StdOut,
                stdErr = result.StdErr,
                durationMs = result.DurationMs,
                timedOut = result.TimedOut,
                elevationDenied = result.ElevationDenied
            });
        }
    }
    catch (OperationCanceledException)
    {
        commandManager.CancelCommand(requestId);
        return Results.Ok(new
        {
            requestId,
            shell,
            command,
            exitCode = -1,
            stdOut = "",
            stdErr = "Komut yürütme zaman aşımına uğradı (" + timeoutSec + " sn).",
            durationMs = timeoutSec * 1000,
            timedOut = true,
            elevationDenied = false
        });
    }
});

/// <summary>
/// Web Konsolundan hedef bilgisayardaki seçili uygulamayı sessizce (silent uninstall) kaldırma (Admin veya Teknisyen girişi gerekir).
/// </summary>
authed.MapPost("/devices/{id:guid}/uninstall-app", async (
    Guid id,
    UninstallAppApiRequest request,
    Microsoft.AspNetCore.SignalR.IHubContext<SignalingHub> hubContext,
    DeviceCommandManager commandManager,
    DeviceRegistry deviceRegistry,
    CancellationToken ct) =>
{
    var device = deviceRegistry.GetById(id);
    if (device is null)
    {
        return Results.NotFound(new { message = "Cihaz bulunamadı." });
    }

    if (!device.IsOnline)
    {
        return Results.BadRequest(new { message = "Cihaz çevrimdışı, kaldırma işlemi başlatılamaz." });
    }

    if (string.IsNullOrWhiteSpace(request.AppName))
    {
        return Results.BadRequest(new { message = "Uygulama adı boş olamaz." });
    }

    string psCommand;
    if (!string.IsNullOrWhiteSpace(request.QuietUninstallString))
    {
        psCommand = $"Start-Process cmd.exe -ArgumentList '/c \"{request.QuietUninstallString.Replace("\"", "\\\"")}\"' -Wait -WindowStyle Hidden";
    }
    else if (!string.IsNullOrWhiteSpace(request.UninstallString))
    {
        var uStr = request.UninstallString.Trim();
        if (uStr.Contains("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            var match = System.Text.RegularExpressions.Regex.Match(uStr, @"\{[0-9a-fA-F\-]{36}\}");
            if (match.Success)
            {
                psCommand = $"Start-Process msiexec.exe -ArgumentList '/x \"{match.Value}\" /qn /norestart' -Wait -WindowStyle Hidden";
            }
            else
            {
                psCommand = $"Start-Process cmd.exe -ArgumentList '/c \"{uStr} /qn /norestart\"' -Wait -WindowStyle Hidden";
            }
        }
        else
        {
            psCommand = $@"
$rawCmd = @'
{uStr}
'@
if ($rawCmd.StartsWith('""')) {{
    $idx = $rawCmd.IndexOf('""', 1)
    if ($idx -gt 1) {{
        $exePath = $rawCmd.Substring(1, $idx - 1)
        $args = $rawCmd.Substring($idx + 1).Trim()
    }} else {{
        $exePath = $rawCmd
        $args = ''
    }}
}} else {{
    $parts = $rawCmd -split ' ', 2
    $exePath = $parts[0]
    $args = if ($parts.Length -gt 1) {{ $parts[1] }} else {{ '' }}
}}
$silentSwitches = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /S /qn /quiet /silent'
Start-Process -FilePath $exePath -ArgumentList ""$args $silentSwitches"".Trim() -Wait -WindowStyle Hidden
";
        }
    }
    else
    {
        var safeAppName = request.AppName.Replace("'", "''");
        psCommand = $@"
$appName = '{safeAppName}'
$app = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*, HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*, HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue | Where-Object {{ $_.DisplayName -eq $appName -or $_.DisplayName -like ""*$appName*"" }} | Select-Object -First 1
if ($app -and $app.QuietUninstallString) {{
    Start-Process cmd.exe -ArgumentList ""/c `""$($app.QuietUninstallString)`"""" -Wait -WindowStyle Hidden
}} elseif ($app -and $app.UninstallString) {{
    if ($app.UninstallString -match '\{{[0-9a-fA-F\-]{{36}}\}}') {{
        $guid = $matches[0]
        Start-Process msiexec.exe -ArgumentList ""/x `""$guid`"" /qn /norestart"" -Wait -WindowStyle Hidden
    }} else {{
        Start-Process cmd.exe -ArgumentList ""/c `""$($app.UninstallString)`"" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /S /qn /quiet"" -Wait -WindowStyle Hidden
    }}
}} else {{
    Get-Package -Name ""*$appName*"" -ErrorAction SilentlyContinue | Uninstall-Package -Force -ErrorAction SilentlyContinue
}}
";
    }

    var requestId = Guid.NewGuid();
    var tcs = commandManager.RegisterCommand(requestId);

    await hubContext.Clients.Group($"device:{id}").SendAsync(
        "ExecuteWebCommand", requestId, "powershell", psCommand, false, ct);

    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(90));

    try
    {
        using (cts.Token.Register(() => commandManager.CancelCommand(requestId)))
        {
            var result = await tcs.Task;
            if (result.ExitCode == 0)
            {
                deviceRegistry.RemoveInstalledApp(id, request.AppName);
            }
            return Results.Ok(new
            {
                success = result.ExitCode == 0,
                appName = request.AppName,
                exitCode = result.ExitCode,
                stdOut = result.StdOut,
                stdErr = result.StdErr,
                message = result.ExitCode == 0 
                    ? $"{request.AppName} uygulaması başarıyla sessizce kaldırıldı." 
                    : $"{request.AppName} kaldırma işlemi tamamlandı (Çıkış Kodu: {result.ExitCode})."
            });
        }
    }
    catch (OperationCanceledException)
    {
        commandManager.CancelCommand(requestId);
        return Results.Ok(new
        {
            success = false,
            appName = request.AppName,
            exitCode = -1,
            stdOut = "",
            stdErr = "Kaldırma işlemi zaman aşımına uğradı (90 sn).",
            message = "Kaldırma işlemi zaman aşımına uğradı ancak arka planda devam ediyor olabilir."
        });
    }
});

/// <summary>
/// İstemcide yürütülen uzak komutların denetim (audit) günlüğünü veritabanına kaydetme.
/// </summary>
app.MapPost("/api/audit/commands", (CommandAuditEntry entry, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory) =>
{
    if (!devices.ValidateAgent(entry.DeviceId, entry.AgentToken))
    {
        return Results.Unauthorized();
    }

    // Boş veya anlamsız komut kaydını reddet
    if (string.IsNullOrWhiteSpace(entry.Command) || entry.Command.Trim().Length < 1)
    {
        return Results.BadRequest(new { message = "Komut boş olamaz." });
    }

    using var db = dbFactory.CreateDbContext();
    db.CommandAudits.Add(new CommandAuditEntity
    {
        Id = Guid.NewGuid(),
        DeviceId = entry.DeviceId,
        SessionId = entry.SessionId,
        Shell = entry.Shell?.Length > 32 ? entry.Shell[..32] : (entry.Shell ?? "cmd"),
        Command = entry.Command.Length > 4000 ? entry.Command[..4000] : entry.Command,
        ExitCode = entry.ExitCode,
        StdOutPreview = (entry.StdOutPreview ?? string.Empty) is { Length: > 2000 } so ? so[..2000] : (entry.StdOutPreview ?? string.Empty),
        StdErrPreview = (entry.StdErrPreview ?? string.Empty) is { Length: > 2000 } se ? se[..2000] : (entry.StdErrPreview ?? string.Empty),
        DurationMs = entry.DurationMs,
        ExecutedAt = entry.ExecutedAt
    });
    db.SaveChanges();

    return Results.NoContent();
}).RequireRateLimiting("agent");

// SignalR Canlı Hub rotası
app.MapHub<SignalingHub>("/hubs/signaling");

// SPA (Single Page Application) yönlendirmesi - React index.html
app.MapFallbackToFile("index.html");

app.Run();

static byte[] CreateNetworkTestPayload(int bytes)
{
    var payload = new byte[bytes];
    var seed = 0x4E65784D;
    for (var i = 0; i < payload.Length; i++)
    {
        seed = unchecked(seed * 1103515245 + 12345);
        payload[i] = (byte)(seed >> 16);
    }
    return payload;
}

static async Task<int> DrainWithLimitAsync(Stream body, int maxBytes)
{
    var buffer = new byte[64 * 1024];
    var total = 0;
    while (true)
    {
        var remaining = maxBytes - total;
        if (remaining <= 0)
        {
            break;
        }

        var read = await body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
        if (read == 0)
        {
            break;
        }

        total += read;
    }

    return total;
}

public sealed record ExecuteCommandApiRequest(
    string? Shell,
    string? Command,
    bool RunAsAdmin = true,
    int? TimeoutSeconds = 30);

public sealed record UninstallAppApiRequest(
    string AppName,
    string? UninstallString = null,
    string? QuietUninstallString = null);
