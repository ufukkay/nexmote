using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
using NexMote.Api.Data;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Production ortamında varsayılan / eksik credential ile başlatmayı engelle
if (builder.Environment.IsProduction())
{
    var adminPassword = builder.Configuration["Admin:Password"];
    var adminApiKey = builder.Configuration["Admin:ApiKey"];
    var enrollmentKey = builder.Configuration["Enrollment:Key"];

    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(adminPassword))
        errors.Add("Admin:Password production ortamında ayarlanmalıdır.");
    if (string.IsNullOrWhiteSpace(adminApiKey))
        errors.Add("Admin:ApiKey production ortamında ayarlanmalıdır.");
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
app.UseRateLimiter();

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
}).RequireRateLimiting("login");

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
/// Kayıtlı tüm cihazların ve donanım metriklerinin listesi (Admin Yetkisi Gerekir).
/// </summary>
admin.MapGet("/devices", (DeviceRegistry devices) => Results.Ok(devices.List()));

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
/// Sunucu anlık performans ve donanım metriklerini (CPU, RAM, Disk, Anlık Ağ Bant Genişliği Mbps) getirme.
/// Yalnızca admin token ile erişilebilir.
/// </summary>
admin.MapGet("/server-metrics", (ServerTelemetryService metrics) => Results.Ok(metrics.GetMetrics()));

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
/// Web Konsolundan doğrudan cihaz üzerinde CMD veya PowerShell komutu çalıştırma (Admin Yetkisi Gerekir).
/// </summary>
admin.MapPost("/devices/{id:guid}/execute-command", async (
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
/// Web Konsolundan hedef bilgisayardaki seçili uygulamayı sessizce (silent uninstall) kaldırma (Admin Yetkisi Gerekir).
/// </summary>
admin.MapPost("/devices/{id:guid}/uninstall-app", async (
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
