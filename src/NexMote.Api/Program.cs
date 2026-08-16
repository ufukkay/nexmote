using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

// SQLite veritabanı bağlantısı ve DbContextFactory kaydı
var dbPath = Path.Combine(AppContext.BaseDirectory, "nexmote.db");
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

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

// SignalR yapılandırması (Görüntü ve dosya aktarımı için maksimum 4 MB mesaj boyutu)
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

// Tekil (Singleton) servislerin bağımlılık enjeksiyonuna kaydı
builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<RemoteSessionRegistry>();
builder.Services.AddSingleton<DownloadCatalog>();

var app = builder.Build();

// Veritabanı tablolarının oluşturulması ve ilk başlangıç ayarlarının kaydedilmesi
using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();

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
}

app.UseCors("web");

// Statik dosyaların ve React web konsolunun (wwwroot) sunulması
app.UseDefaultFiles();
app.UseStaticFiles();

/// <summary>
/// Sunucu sağlık kontrol endpoint'i.
/// </summary>
app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

/// <summary>
/// Admin e-posta ve şifresini doğrulayarak korumalı API istekleri için Bearer ApiKey token'ı döner.
/// </summary>
app.MapPost("/api/auth/login", (AdminLoginRequest request, IConfiguration config) =>
{
    var expectedEmail = config["Admin:Email"] ?? "admin@nexmote.com";
    var expectedPassword = config["Admin:Password"] ?? "admin123";
    var apiKey = config["Admin:ApiKey"];

    var isValid = string.Equals(request.Email, expectedEmail, StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(request.Password, expectedPassword, StringComparison.Ordinal);

    if (!isValid || string.IsNullOrEmpty(apiKey))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new AdminLoginResponse(apiKey));
});

// Admin ve Teknisyen korumalı rota grubu (AdminAuthFilter ile Bearer token doğrulanır)
var admin = app.MapGroup("/api").AddEndpointFilter<AdminAuthFilter>();

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

    var enrolled = devices.Enroll(request);
    return Results.Ok(enrolled);
});

/// <summary>
/// İstemcinin periyodik 20s canlılık bildirimi ve donanım telemetrisi (CPU, RAM, Disk, Uptime).
/// </summary>
app.MapPost("/api/agents/{deviceId:guid}/heartbeat", (Guid deviceId, DeviceHeartbeatRequest request, DeviceRegistry devices) =>
{
    return devices.Heartbeat(deviceId, request)
        ? Results.NoContent()
        : Results.NotFound(new { message = "Cihaz bulunamadı veya güvenlik token'ı geçersiz." });
});

/// <summary>
/// Kayıtlı tüm cihazların ve donanım metriklerinin listesi (Admin Yetkisi Gerekir).
/// </summary>
admin.MapGet("/devices", (DeviceRegistry devices) => Results.Ok(devices.List()));

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
    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode));
});

/// <summary>
/// Sunucu genel ayarlarını güncelleme (Admin Yetkisi Gerekir).
/// </summary>
admin.MapPost("/settings", (ServerSettingsContract request, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.First();
    setting.ServerUrl = request.ServerUrl.TrimEnd('/');
    setting.EnrollmentKey = request.EnrollmentKey;
    setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
    setting.DefaultLocationCode = request.DefaultLocationCode;
    setting.UpdatedAt = DateTimeOffset.UtcNow;

    db.SaveChanges();
    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode));
});

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

/// <summary>
/// Seçili çevrimiçi cihaza uzaktan sessiz Agent MSI güncelleme sinyali gönderme (Admin Yetkisi Gerekir).
/// </summary>
admin.MapPost("/agents/{deviceId:guid}/update", async (Guid deviceId, Microsoft.AspNetCore.SignalR.IHubContext<SignalingHub> hub, DeviceRegistry devices, IConfiguration config) =>
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
/// Tekil cihaz detayını getirme (Admin Yetkisi Gerekir).
/// </summary>
admin.MapGet("/devices/{deviceId:guid}", (Guid deviceId, DeviceRegistry devices) =>
{
    var device = devices.Get(deviceId);
    return device is null ? Results.NotFound() : Results.Ok(device);
});

/// <summary>
/// Teknisyen için nexmote:// deep-link bağlantı oturumu oluşturma (Admin Yetkisi Gerekir).
/// </summary>
admin.MapPost("/remote-sessions", (CreateRemoteSessionRequest request, HttpContext http, DeviceRegistry devices, RemoteSessionRegistry sessions, IConfiguration config) =>
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
/// İstemcide yürütülen uzak komutların denetim (audit) günlüğünü veritabanına kaydetme.
/// </summary>
app.MapPost("/api/audit/commands", (CommandAuditEntry entry, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory) =>
{
    if (!devices.ValidateAgent(entry.DeviceId, entry.AgentToken))
    {
        return Results.Unauthorized();
    }

    using var db = dbFactory.CreateDbContext();
    db.CommandAudits.Add(new CommandAuditEntity
    {
        Id = Guid.NewGuid(),
        DeviceId = entry.DeviceId,
        SessionId = entry.SessionId,
        Shell = entry.Shell,
        Command = entry.Command.Length > 4000 ? entry.Command[..4000] : entry.Command,
        ExitCode = entry.ExitCode,
        StdOutPreview = entry.StdOutPreview.Length > 2000 ? entry.StdOutPreview[..2000] : entry.StdOutPreview,
        StdErrPreview = entry.StdErrPreview.Length > 2000 ? entry.StdErrPreview[..2000] : entry.StdErrPreview,
        DurationMs = entry.DurationMs,
        ExecutedAt = entry.ExecutedAt
    });
    db.SaveChanges();

    return Results.NoContent();
});

// SignalR Canlı Hub rotası
app.MapHub<SignalingHub>("/hubs/signaling");

// SPA (Single Page Application) yönlendirmesi - React index.html
app.MapFallbackToFile("index.html");

app.Run();
