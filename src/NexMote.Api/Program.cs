using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Endpoints;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// SQLite veritabanı bağlantısı ve DbContextFactory kaydı
var dbPath = Path.Combine(AppContext.BaseDirectory, "nexmote.db");

// Production ortamında varsayılan / eksik credential ile başlatmayı engelle
if (builder.Environment.IsProduction())
{
    var adminPassword = builder.Configuration["Admin:Password"];
    var enrollmentKey = builder.Configuration["Enrollment:Key"];
    var publicUrl = builder.Configuration["PublicUrl"];
    var manifestSignatureRequired = builder.Configuration.GetValue("Updates:ManifestSignature:Required", false);

    var isPrivateDeployment = string.IsNullOrWhiteSpace(publicUrl) ||
        (Uri.TryCreate(publicUrl, UriKind.Absolute, out var pUri) &&
         (pUri.IsLoopback ||
          pUri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
          (IPAddress.TryParse(pUri.Host, out var ip) && (
              IPAddress.IsLoopback(ip) ||
              ip.GetAddressBytes()[0] == 10 ||
              (ip.GetAddressBytes()[0] == 172 && ip.GetAddressBytes()[1] >= 16 && ip.GetAddressBytes()[1] <= 31) ||
              (ip.GetAddressBytes()[0] == 192 && ip.GetAddressBytes()[1] == 168)))));

    if (isPrivateDeployment)
    {
        Console.WriteLine($"[NexMote] Özel/Yerel ağ veya IIS dağıtımı ({publicUrl ?? "LAN"}). Esnek konfigürasyon aktif.");
    }
    else
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(adminPassword) && !File.Exists(dbPath))
            errors.Add("Admin:Password production ortamında ayarlanmalıdır (ilk Admin kullanıcısının bootstrap şifresi).");
        if (string.IsNullOrWhiteSpace(enrollmentKey) && !File.Exists(dbPath))
            errors.Add("Enrollment:Key production ortamında ayarlanmalıdır.");
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var publicUri) ||
            !string.Equals(publicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            errors.Add("PublicUrl production ortamında HTTPS olmalıdır.");
        if (!manifestSignatureRequired)
            errors.Add("Updates:ManifestSignature production ortamında zorunlu olmalıdır.");

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
}
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
builder.Services.AddSingleton<EnrollmentKeyValidator>();
builder.Services.AddSingleton<AlertService>();
builder.Services.AddHostedService<AlertMonitorService>();

// Web ön yüzü ve teknisyen istemcisi için CORS politikası
builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        var configuredOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ??
            ["http://localhost:5173", "http://127.0.0.1:5173", "https://nexmote.com", "https://www.nexmote.com", "http://192.168.0.219"];

        policy
            .SetIsOriginAllowed(origin => configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
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
    options.AddPolicy("login", httpContext =>
    {
        var key = GetClientRateLimitKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"login:{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Heartbeat & audit — cihaz kimliği varsa cihaz başına, yoksa IP başına 120 istek/dakika.
    options.AddPolicy("agent", httpContext =>
    {
        var deviceKey = httpContext.Request.RouteValues.TryGetValue("deviceId", out var deviceId) && deviceId is not null
            ? deviceId.ToString()
            : GetClientRateLimitKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"agent:{deviceKey}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // Genel API — IP başına 300 istek/dakika
    options.AddPolicy("api", httpContext =>
    {
        var key = GetClientRateLimitKey(httpContext);
        return RateLimitPartition.GetSlidingWindowLimiter($"api:{key}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
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
builder.Services.AddSingleton<ManifestSignatureVerifier>();
builder.Services.AddSingleton<DownloadCatalog>();
builder.Services.AddSingleton<ServerTelemetryService>();
builder.Services.AddSingleton<DeviceCommandManager>();
builder.Services.AddSingleton<DeviceCommandQueue>();
builder.Services.AddSingleton<AuditLogService>();
builder.Services.AddSingleton<DatabaseMaintenanceService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DatabaseMaintenanceService>());

var app = builder.Build();

// Veritabanı tablolarının oluşturulması, şema güncellemeleri ve ilk başlangıç ayarlarının kaydedilmesi
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();

    // Idempotent şema başlatıcı ve kolon migration yöneticisi (Madde 5)
    DatabaseInitializer.Initialize(db, app.Logger);

    // Ajan Ana Yasası Madde 9 (Self-Healing Identity): Açılışta mükerrer cihazları otomatik tekilleştir
    var deviceRegistry = scope.ServiceProvider.GetRequiredService<DeviceRegistry>();
    var dedupCount = deviceRegistry.DeduplicateDevices();
    if (dedupCount > 0)
    {
        app.Logger.LogInformation("Mükerrer cihazlar otomatik tekilleştirildi. Temizlenen kayıt sayısı: {Count}", dedupCount);
    }

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
    var bootstrapPassword = builder.Configuration["Admin:Password"];
    if (string.IsNullOrWhiteSpace(bootstrapPassword))
    {
        bootstrapPassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        Console.WriteLine($"Development bootstrap Admin password was not configured. One-time password: {bootstrapPassword}");
    }
    UserAuthService.EnsureBootstrapAdmin(db, passwordHasher, bootstrapEmail, bootstrapPassword);
}

app.UseMiddleware<CorrelationIdMiddleware>();

// Tüm HTTP ve API yanıtlarında UTF-8 karakter kodlaması güvencesi
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

    context.Response.OnStarting(() =>
    {
        var contentType = context.Response.ContentType;
        if (!string.IsNullOrEmpty(contentType) &&
            (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
             contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) &&
            !contentType.Contains("charset", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.ContentType = contentType + "; charset=utf-8";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
    KnownProxies = { IPAddress.Loopback, IPAddress.IPv6Loopback }
});

if (app.Environment.IsProduction())
{
    app.Use(async (context, next) =>
    {
        // Yalnızca harici/internet domain isteklerinde HTTPS'e yönlendir; yerel ağ / özel IP (192.168.x vb.) isteklerini zorlama
        if (!context.Request.IsHttps && !NexMote.Shared.Network.NexMoteHttp.IsPrivateOrLocalHost(context.Request.Host.Host))
        {
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            var httpsUrl = "https://" + context.Request.Host + context.Request.PathBase + context.Request.Path + context.Request.QueryString;
            context.Response.Redirect(httpsUrl, permanent: true);
            return;
        }
        await next();
    });
}

app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Statik dosyaların ve React web konsolunun (wwwroot) sunulması
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var contentType = ctx.Context.Response.ContentType;
        if (!string.IsNullOrEmpty(contentType) &&
            (contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
             contentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase) ||
             contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase)) &&
            !contentType.Contains("charset", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.ContentType = contentType + "; charset=utf-8";
        }
    }
});

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
app.MapHub<SignalingHub>("/hubs/signaling", options =>
{
    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                         Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
});

// SPA (Single Page Application) yönlendirmesi - React index.html
app.MapFallbackToFile("index.html");

app.Run();

static string GetClientRateLimitKey(HttpContext httpContext)
{
    return httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
