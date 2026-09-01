using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app, RouteGroupBuilder authed, RouteGroupBuilder admin)
    {
        // Public / Agent Device endpoints (Zero-Touch Auto-Enrollment)
        app.MapPost("/api/agents/enroll", (AgentEnrollmentRequest request, DeviceRegistry devices, DeviceGroupService groups) =>
        {
            if (string.IsNullOrWhiteSpace(request.DeviceName))
            {
                return Results.BadRequest(new { message = "DeviceName zorunludur." });
            }

            Guid? targetGroupId = null;
            if (!string.IsNullOrWhiteSpace(request.EnrollmentKey))
            {
                var matchedGroup = groups.FindByEnrollmentKey(request.EnrollmentKey);
                if (matchedGroup is not null)
                {
                    targetGroupId = matchedGroup.Id;
                }
            }

            try
            {
                var enrolled = devices.Enroll(request, targetGroupId);
                return Results.Ok(enrolled);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: 403);
            }
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/heartbeat", (Guid deviceId, DeviceHeartbeatRequest request, DeviceRegistry devices) =>
        {
            return devices.Heartbeat(deviceId, request)
                ? Results.NoContent()
                : Results.NotFound(new { message = "Cihaz bulunamadı veya güvenlik token'ı geçersiz." });
        }).RequireRateLimiting("agent");

        app.MapPost("/api/audit/commands", (CommandAuditEntry entry, DeviceRegistry devices, IDbContextFactory<AppDbContext> dbFactory) =>
        {
            if (!devices.ValidateAgent(entry.DeviceId, entry.AgentToken))
            {
                return Results.Unauthorized();
            }

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

        // Authed Device endpoints
        authed.MapGet("/devices", (DeviceRegistry devices) => Results.Ok(devices.List()));

        authed.MapGet("/devices/{deviceId:guid}", (Guid deviceId, DeviceRegistry devices) =>
        {
            var device = devices.Get(deviceId);
            return device is null ? Results.NotFound() : Results.Ok(device);
        });

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

        authed.MapPost("/agents/{deviceId:guid}/update", async (Guid deviceId, IHubContext<SignalingHub> hub, DeviceRegistry devices, IConfiguration config) =>
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

        authed.MapPost("/devices/{id:guid}/execute-command", async (
            Guid id,
            ExecuteCommandApiRequest request,
            IHubContext<SignalingHub> hubContext,
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

            await hubContext.Clients.Group($"device:{id}").SendAsync(
                "ExecuteWebCommand", requestId, shell, command, false, ct);

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

        authed.MapPost("/devices/{id:guid}/uninstall-app", async (
            Guid id,
            UninstallAppApiRequest request,
            IHubContext<SignalingHub> hubContext,
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

        // Admin Device endpoints
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

        // Network Test Endpoints
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
    }

    private static byte[] CreateNetworkTestPayload(int bytes)
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

    private static async Task<int> DrainWithLimitAsync(Stream body, int maxBytes)
    {
        var buffer = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var remaining = maxBytes - total;
            if (remaining <= 0) break;

            var read = await body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)));
            if (read == 0) break;

            total += read;
        }
        return total;
    }
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
