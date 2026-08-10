using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Path.Combine(AppContext.BaseDirectory, "nexmote.db");
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173", "http://127.0.0.1:5173"])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 4 * 1024 * 1024;
});

builder.Services.AddSingleton<DeviceRegistry>();
builder.Services.AddSingleton<RemoteSessionRegistry>();
builder.Services.AddSingleton<DownloadCatalog>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbFactory.CreateDbContext();
    db.Database.EnsureCreated();

    if (!db.ServerSettings.Any())
    {
        var bootstrapUrl = builder.Configuration["PublicUrl"] ?? "http://127.0.0.1:5080";
        var bootstrapSetting = new ServerSettingEntity
        {
            ServerUrl = bootstrapUrl,
            EnrollmentKey = builder.Configuration["Enrollment:Key"] ?? "dev-enrollment-key",
            HeartbeatSeconds = 20,
            DefaultLocationCode = "OFFICE",
            TechnicianKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ServerSettings.Add(bootstrapSetting);
        db.SaveChanges();

        var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "nexmote-first-run-credentials.txt");
        File.WriteAllText(bootstrapPath,
            "NexMote ilk kurulum kimlik bilgileri" + Environment.NewLine +
            "Bu dosya yalnizca bir kez, ilk baslatmada olusturulur. Guvenli bir yerde saklayin." + Environment.NewLine +
            Environment.NewLine +
            $"Teknisyen Erisim Anahtari (X-Technician-Key): {bootstrapSetting.TechnicianKey}" + Environment.NewLine +
            $"Enrollment Anahtari: {bootstrapSetting.EnrollmentKey}" + Environment.NewLine);

        app.Logger.LogWarning(
            "Ilk kurulum tespit edildi. Teknisyen Erisim Anahtari uretildi ve {Path} dosyasina yazildi. " +
            "Web panele/Technician App'e giris icin bu anahtar gereklidir.",
            bootstrapPath);
    }
}

app.UseCors("web");

static bool IsTechnicianAuthorized(HttpContext http, AppDbContext db)
{
    var setting = db.ServerSettings.AsNoTracking().FirstOrDefault();
    if (setting is null || string.IsNullOrEmpty(setting.TechnicianKey))
    {
        // Bootstrap window: no settings row yet means the app just started and
        // hasn't finished provisioning. Startup always creates one before serving
        // requests, so this only trips if something bypassed that step.
        return true;
    }

    var provided = http.Request.Headers["X-Technician-Key"].FirstOrDefault();
    if (string.IsNullOrEmpty(provided))
    {
        return false;
    }

    var providedBytes = Encoding.UTF8.GetBytes(provided);
    var expectedBytes = Encoding.UTF8.GetBytes(setting.TechnicianKey);
    return providedBytes.Length == expectedBytes.Length &&
           CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
}

app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

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

app.MapPost("/api/agents/{deviceId:guid}/heartbeat", (Guid deviceId, DeviceHeartbeatRequest request, DeviceRegistry devices) =>
{
    return devices.Heartbeat(deviceId, request)
        ? Results.NoContent()
        : Results.NotFound(new { message = "Device not found or token invalid." });
});

app.MapGet("/api/devices", (HttpContext http, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    if (!IsTechnicianAuthorized(http, db))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(devices.List());
});

app.MapGet("/api/downloads", (DownloadCatalog downloads) => Results.Ok(downloads.List()));

app.MapGet("/api/settings", (HttpContext http, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    if (!IsTechnicianAuthorized(http, db))
    {
        return Results.Unauthorized();
    }

    var setting = db.ServerSettings.AsNoTracking().First();
    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode, setting.TechnicianKey));
});

app.MapPost("/api/settings", (HttpContext http, ServerSettingsContract request, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    if (!IsTechnicianAuthorized(http, db))
    {
        return Results.Unauthorized();
    }

    var setting = db.ServerSettings.First();
    setting.ServerUrl = request.ServerUrl.TrimEnd('/');
    setting.EnrollmentKey = request.EnrollmentKey;
    setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
    setting.DefaultLocationCode = request.DefaultLocationCode;
    if (!string.IsNullOrWhiteSpace(request.TechnicianKey))
    {
        setting.TechnicianKey = request.TechnicianKey;
    }
    setting.UpdatedAt = DateTimeOffset.UtcNow;

    db.SaveChanges();
    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode, setting.TechnicianKey));
});

app.MapPost("/api/downloads/generate", (HttpContext http, ServerSettingsContract request, IHostEnvironment env, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using (var authDb = dbFactory.CreateDbContext())
    {
        if (!IsTechnicianAuthorized(http, authDb))
        {
            return Results.Unauthorized();
        }
    }

    try
    {
        var scriptPath = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", "scripts", "package-windows.ps1"));
        if (!File.Exists(scriptPath))
        {
            scriptPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", "package-windows.ps1"));
        }

        var psi = new System.Diagnostics.ProcessStartInfo("powershell", $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -ServerUrl \"{request.ServerUrl}\" -EnrollmentKey \"{request.EnrollmentKey}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = System.Diagnostics.Process.Start(psi);
        process?.WaitForExit(30000);

        return Results.Ok(new { message = "Paketler başarıyla güncellendi.", serverUrl = request.ServerUrl });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Paket oluşturulamadı: {ex.Message}");
    }
});

app.MapGet("/downloads/{fileName}", (string fileName, DownloadCatalog downloads) =>
{
    var file = downloads.GetFile(fileName);
    return file is null
        ? Results.NotFound(new { message = "Download package not found." })
        : Results.File(file.Path, file.ContentType, file.FileName);
});

app.MapGet("/api/devices/{deviceId:guid}", (Guid deviceId, HttpContext http, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    if (!IsTechnicianAuthorized(http, db))
    {
        return Results.Unauthorized();
    }

    var device = devices.Get(deviceId);
    return device is null ? Results.NotFound() : Results.Ok(device);
});

app.MapPost("/api/remote-sessions", (CreateRemoteSessionRequest request, HttpContext http, DeviceRegistry devices, RemoteSessionRegistry sessions, IConfiguration config, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    if (!IsTechnicianAuthorized(http, db))
    {
        return Results.Unauthorized();
    }

    var device = devices.Get(request.DeviceId);
    if (device is null)
    {
        return Results.NotFound(new { message = "Device not found." });
    }

    if (!device.IsOnline)
    {
        return Results.BadRequest(new { message = "Device is offline." });
    }

    var serverUrl = config["PublicUrl"];
    if (string.IsNullOrWhiteSpace(serverUrl))
    {
        serverUrl = $"{http.Request.Scheme}://{http.Request.Host}";
    }

    var technicianKey = http.Request.Headers["X-Technician-Key"].FirstOrDefault() ?? string.Empty;
    return Results.Ok(sessions.Create(request.DeviceId, serverUrl, technicianKey));
});

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

app.MapHub<SignalingHub>("/hubs/signaling");

app.Run();
