using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("web", policy =>
    {
        policy
            .WithOrigins(builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
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

app.UseCors("web");

app.MapGet("/health", () => Results.Ok(new { product = "NexMote", status = "ok", at = DateTimeOffset.UtcNow }));

app.MapPost("/api/agents/enroll", (AgentEnrollmentRequest request, DeviceRegistry devices, IConfiguration config) =>
{
    var expectedKey = config["Enrollment:Key"] ?? "dev-enrollment-key";
    if (!string.Equals(request.EnrollmentKey, expectedKey, StringComparison.Ordinal))
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
