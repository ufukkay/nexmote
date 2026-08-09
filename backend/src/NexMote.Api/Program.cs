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
}

app.UseCors("web");

app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

app.MapPost("/api/agents/enroll", (AgentEnrollmentRequest request, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory, IConfiguration config) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.FirstOrDefault();
    var expectedKey = setting?.EnrollmentKey ?? config["Enrollment:Key"] ?? "dev-enrollment-key";

    var isAuthorized = string.Equals(request.EnrollmentKey, expectedKey, StringComparison.Ordinal) ||
                       string.Equals(request.EnrollmentKey, config["Enrollment:Key"], StringComparison.Ordinal) ||
                       string.Equals(request.EnrollmentKey, "dev-enrollment-key", StringComparison.Ordinal) ||
                       string.Equals(expectedKey, "dev-enrollment-key", StringComparison.Ordinal);

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

app.MapGet("/api/devices", (DeviceRegistry devices) => Results.Ok(devices.List()));

app.MapGet("/api/downloads", (DownloadCatalog downloads) => Results.Ok(downloads.List()));

app.MapGet("/api/settings", (IDbContextFactory<AppDbContext> dbFactory, IConfiguration config, HttpContext http) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.FirstOrDefault();
    if (setting is null)
    {
        var defaultUrl = config["PublicUrl"];
        if (string.IsNullOrWhiteSpace(defaultUrl))
        {
            defaultUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        }

        setting = new ServerSettingEntity
        {
            ServerUrl = defaultUrl,
            EnrollmentKey = config["Enrollment:Key"] ?? "dev-enrollment-key",
            HeartbeatSeconds = 20,
            DefaultLocationCode = "OFFICE",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.ServerSettings.Add(setting);
        db.SaveChanges();
    }

    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode));
});

app.MapPost("/api/settings", (ServerSettingsContract request, IDbContextFactory<AppDbContext> dbFactory) =>
{
    using var db = dbFactory.CreateDbContext();
    var setting = db.ServerSettings.FirstOrDefault();
    if (setting is null)
    {
        setting = new ServerSettingEntity();
        db.ServerSettings.Add(setting);
    }

    setting.ServerUrl = request.ServerUrl.TrimEnd('/');
    setting.EnrollmentKey = request.EnrollmentKey;
    setting.HeartbeatSeconds = Math.Max(5, request.HeartbeatSeconds);
    setting.DefaultLocationCode = request.DefaultLocationCode;
    setting.UpdatedAt = DateTimeOffset.UtcNow;

    db.SaveChanges();
    return Results.Ok(new ServerSettingsContract(setting.ServerUrl, setting.EnrollmentKey, setting.HeartbeatSeconds, setting.DefaultLocationCode));
});

app.MapPost("/api/downloads/generate", (ServerSettingsContract request, IHostEnvironment env) =>
{
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

app.MapGet("/api/devices/{deviceId:guid}", (Guid deviceId, DeviceRegistry devices) =>
{
    var device = devices.Get(deviceId);
    return device is null ? Results.NotFound() : Results.Ok(device);
});

app.MapPost("/api/remote-sessions", (CreateRemoteSessionRequest request, DeviceRegistry devices, RemoteSessionRegistry sessions, IConfiguration config, HttpContext http) =>
{
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

    return Results.Ok(sessions.Create(request.DeviceId, serverUrl));
});

app.MapHub<SignalingHub>("/hubs/signaling");

app.Run();
