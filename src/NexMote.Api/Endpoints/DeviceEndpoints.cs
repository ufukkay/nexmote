using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Auth;
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
        app.MapPost("/api/agents/enroll", (AgentEnrollmentRequest request, DeviceRegistry devices, EnrollmentKeyValidator enrollmentKeys) =>
        {
            if (string.IsNullOrWhiteSpace(request.DeviceName))
            {
                return Results.BadRequest(new { message = "DeviceName zorunludur." });
            }

            var authorization = enrollmentKeys.Authorize(request.EnrollmentKey);
            if (!authorization.IsAuthorized)
            {
                return Results.Unauthorized();
            }

            try
            {
                var enrolled = devices.Enroll(request, authorization.GroupId);
                var hub = app.Services.GetRequiredService<IHubContext<SignalingHub>>();
                _ = hub.Clients.Group("devices:feed").SendAsync("DeviceEnrolledDelta", enrolled.DeviceId);
                return Results.Ok(enrolled);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { message = ex.Message }, statusCode: 403);
            }
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/heartbeat", (
            Guid deviceId,
            DeviceHeartbeatRequest request,
            DeviceRegistry devices,
            IHubContext<SignalingHub> hub) =>
        {
            var updated = devices.Heartbeat(deviceId, request);
            if (!updated)
            {
                return Results.NotFound(new { message = "Cihaz bulunamadı veya güvenlik token'ı geçersiz." });
            }

            // Gerçek zamanlı SignalR delta telemetri yayını (Madde 7)
            var delta = new DeviceDeltaUpdate(
                deviceId,
                IsOnline: true,
                request.ActiveUser,
                request.IpAddress,
                request.CpuUsagePercent,
                request.MemoryTotalMb,
                request.MemoryUsedMb,
                request.DiskFreeMb,
                request.UptimeSeconds,
                request.AgentVersion,
                DateTimeOffset.UtcNow);

            _ = hub.Clients.Group("devices:feed").SendAsync("DeviceTelemetryDelta", delta);

            return Results.NoContent();
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

        app.MapGet("/api/agents/{deviceId:guid}/commands/next", (Guid deviceId, string agentToken, DeviceRegistry devices, DeviceCommandQueue commandQueue) =>
        {
            if (!devices.ValidateAgent(deviceId, agentToken))
            {
                return Results.Unauthorized();
            }

            var command = commandQueue.TakeNext(deviceId);
            return command is null ? Results.NoContent() : Results.Ok(command);
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/commands/{requestId:guid}/result", (
            Guid deviceId,
            Guid requestId,
            AgentQueuedCommandResult request,
            DeviceRegistry devices,
            DeviceCommandQueue commandQueue,
            DeviceCommandManager commandManager) =>
        {
            if (!devices.ValidateAgent(deviceId, request.AgentToken))
            {
                return Results.Unauthorized();
            }

            var result = new DeviceCommandExecutionResult(
                requestId,
                request.ExitCode,
                request.StdOut,
                request.StdErr,
                request.DurationMs,
                request.TimedOut,
                request.ElevationDenied);

            var kind = commandQueue.GetKind(requestId);
            if (!commandQueue.Complete(deviceId, result))
            {
                return Results.Conflict(new { error = "Command result is unknown or conflicts with the recorded result." });
            }
            commandManager.CompleteCommand(deviceId, result);
            if (string.Equals(kind, "agent-uninstall", StringComparison.OrdinalIgnoreCase) && result.ExitCode == 0)
            {
                devices.Delete(deviceId);
            }
            return Results.NoContent();
        }).RequireRateLimiting("agent");

        // Authed Device endpoints (Madde 7: Server-side Pagination, Sort & Filter)
        authed.MapGet("/devices/paged", ([AsParameters] DeviceQueryOptions options, DeviceRegistry devices) =>
        {
            return Results.Ok(devices.ListPaged(options));
        });

        authed.MapGet("/devices", (
            [AsParameters] DeviceQueryOptions options,
            HttpContext http,
            DeviceRegistry devices) =>
        {
            if (http.Request.Query.ContainsKey("page") ||
                http.Request.Query.ContainsKey("pageSize") ||
                http.Request.Query.ContainsKey("search") ||
                http.Request.Query.ContainsKey("status") ||
                http.Request.Query.ContainsKey("sortBy") ||
                http.Request.Query.ContainsKey("groupId"))
            {
                return Results.Ok(devices.ListPaged(options));
            }

            return Results.Ok(devices.List());
        });

        authed.MapGet("/devices/{deviceId:guid}", (Guid deviceId, DeviceRegistry devices) =>
        {
            var device = devices.Get(deviceId);
            return device is null ? Results.NotFound() : Results.Ok(device);
        });

        authed.MapPost("/remote-sessions", (CreateRemoteSessionRequest request, HttpContext http, DeviceRegistry devices, RemoteSessionRegistry sessions, AuditLogService auditLog, IConfiguration config) =>
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

            if (Uri.TryCreate(serverUrl, UriKind.Absolute, out var parsedServerUri))
            {
                var host = parsedServerUri.Host;
                if (host.Equals("nexmote.com", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("www.nexmote.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".nexmote.com", StringComparison.OrdinalIgnoreCase))
                {
                    serverUrl = "https://nexmote.com";
                }
            }

            var session = sessions.Create(request.DeviceId, serverUrl);
            auditLog.Log(http, "session.start", "Device", device.Id.ToString(), new { device.DeviceName, device.LocationCode, serverUrl, sessionId = session.SessionId });
            return Results.Ok(session);
        });

        authed.MapPost("/agents/{deviceId:guid}/update", async (Guid deviceId, HttpContext http, IHubContext<SignalingHub> hub, DeviceRegistry devices, AuditLogService auditLog, IConfiguration config) =>
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
            await hub.Clients.Group($"device:{deviceId}:service").SendAsync("RemoteUpdateRequested", msiUrl);
            auditLog.Log(http, "agent.update_requested", "Device", deviceId.ToString(), new { device.DeviceName, msiUrl });
            return Results.Ok(new { message = "Sessiz Agent güncelleme sinyali cihaza başarıyla iletildi." });
        });

        authed.MapPost("/devices/{id:guid}/execute-command", async (
            Guid id,
            ExecuteCommandApiRequest request,
            HttpContext http,
            ClaimsPrincipal user,
            IHubContext<SignalingHub> hubContext,
            DeviceCommandManager commandManager,
            DeviceCommandQueue commandQueue,
            DeviceRegistry deviceRegistry,
            SecurityProfileService securityProfiles,
            AuditLogService auditLog,
            SignalSessionAccess signalSessionAccess,
            CancellationToken ct) =>
        {
            var device = deviceRegistry.GetById(id);
            if (device is null)
            {
                return Results.NotFound(new { message = "Cihaz bulunamadı." });
            }

            if (string.IsNullOrWhiteSpace(request.Command))
            {
                return Results.BadRequest(new { message = "Komut boş olamaz." });
            }

            if (request.Command.Length > 16_384)
            {
                return Results.BadRequest(new { message = "Komut metni izin verilen maksimum boyutu (16.384 karakter) aşıyor." });
            }

            // Sunucu tarafı güvenlik profili kısıtlaması (Madde 15)
            var profile = securityProfiles.GetEffectiveProfile(id);
            if (profile != null && !profile.AllowRemoteTerminal)
            {
                auditLog.Log(http, "command.execute", "Device", id.ToString(), new
                {
                    blocked = true,
                    reason = "Cihazın güvenlik profili uzak terminal komut çalıştırmayı engelliyor."
                }, success: false);
                return Results.Json(new
                {
                    message = "Bu cihazın güvenlik profili uzak terminal komut çalıştırmayı engellemektedir."
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var requestId = Guid.NewGuid();
            var initiatorIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? initiatorUserId = Guid.TryParse(initiatorIdStr, out var pId) ? pId : null;
            var initiatorEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
            var corrId = http.GetCorrelationId();

            var shellReq = (request.Shell ?? "powershell").Trim().ToLowerInvariant();
            if (shellReq != "powershell" && shellReq != "cmd" && shellReq != "pwsh")
            {
                return Results.BadRequest(new { message = "Desteklenmeyen kabuk türü. Yalnızca 'powershell' veya 'cmd' kullanılabilir." });
            }
            var shell = shellReq == "cmd" ? "cmd" : "powershell";
            var command = request.Command.Trim();
            var timeoutSec = Math.Clamp(request.TimeoutSeconds ?? 30, 5, 120);
            commandQueue.Enqueue(requestId, id, "command", shell, command, timeoutSec, initiatorUserId, initiatorEmail, corrId);

            auditLog.Log(http, "command.execute", "Device", id.ToString(), new
            {
                requestId,
                shell,
                command,
                device.DeviceName,
                device.IsOnline
            });

            if (!device.IsOnline)
            {
                return Results.Json(new
                {
                    requestId,
                    shell,
                    command,
                    queued = true,
                    exitCode = 202,
                    stdOut = "",
                    stdErr = "Cihaz çevrimdışı. Komut kuyruğa alındı ve cihaz çevrimiçi olduğunda çalıştırılacak.",
                    durationMs = 0,
                    timedOut = false,
                    elevationDenied = false
                }, statusCode: StatusCodes.Status202Accepted);
            }

            var tcs = commandManager.RegisterCommand(requestId, id);
            commandQueue.MarkDelivered(requestId);

            var execShell = shell;
            var execCommand = command;
            if (string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase))
            {
                var utf8Preamble = "$OutputEncoding = [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); $ProgressPreference = 'SilentlyContinue'; ";
                var bytes = Encoding.Unicode.GetBytes(utf8Preamble + command);
                var base64 = Convert.ToBase64String(bytes);
                execShell = "cmd";
                execCommand = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {base64}";
            }
            else if (string.Equals(shell, "cmd", StringComparison.OrdinalIgnoreCase))
            {
                if (!execCommand.StartsWith("chcp ", StringComparison.OrdinalIgnoreCase))
                {
                    execCommand = "chcp 65001 >nul & " + execCommand;
                }
            }

            var targetGroup = signalSessionAccess.HasServiceConnection(id)
                ? $"device:{id}:service"
                : $"device:{id}";

            await hubContext.Clients.Group(targetGroup).SendAsync(
                "ExecuteWebCommand", requestId, execShell, execCommand, false, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

            try
            {
                using (cts.Token.Register(() => commandManager.CancelCommand(requestId)))
                {
                    var result = await tcs.Task;
                    commandQueue.Complete(id, result);
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
                commandQueue.MarkTimedOut(requestId, timeoutSec);
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
            HttpContext http,
            ClaimsPrincipal user,
            IHubContext<SignalingHub> hubContext,
            DeviceCommandManager commandManager,
            DeviceCommandQueue commandQueue,
            DeviceRegistry deviceRegistry,
            AuditLogService auditLog,
            SignalSessionAccess signalSessionAccess,
            CancellationToken ct) =>
        {
            var device = deviceRegistry.GetById(id);
            if (device is null)
            {
                return Results.NotFound(new { message = "Cihaz bulunamadı." });
            }

            if (string.IsNullOrWhiteSpace(request.AppName))
            {
                return Results.BadRequest(new { message = "Uygulama adı boş olamaz." });
            }

            if (request.AppName.Length > 256)
            {
                return Results.BadRequest(new { message = "Uygulama adı en fazla 256 karakter olabilir." });
            }

            if ((request.UninstallString?.Length ?? 0) > 2048 || (request.QuietUninstallString?.Length ?? 0) > 2048)
            {
                return Results.BadRequest(new { message = "Kaldırma parametresi izin verilen maksimum boyutu aşıyor." });
            }

            var safeAppName = (request.AppName ?? string.Empty).Replace("'", "''");
            var safeUninstallString = (request.UninstallString ?? string.Empty).Replace("'", "''");
            var safeQuietString = (request.QuietUninstallString ?? string.Empty).Replace("'", "''");

            var psCommand = $$"""
$ErrorActionPreference = 'SilentlyContinue'
$ProgressPreference = 'SilentlyContinue'
$OutputEncoding = [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
$appName = '{{safeAppName}}'
$rawCmd = '{{safeUninstallString}}'.Trim()
$quietCmd = '{{safeQuietString}}'.Trim()

function Parse-Cmd([string]$str) {
    $str = $str.Trim()
    if ($str.StartsWith('"')) {
        $endQ = $str.IndexOf('"', 1)
        if ($endQ -gt 1) {
            return [PSCustomObject]@{
                Exe = $str.Substring(1, $endQ - 1).Trim()
                Args = $str.Substring($endQ + 1).Trim()
            }
        }
    }
    $idx = $str.IndexOf('.exe', [System.StringComparison]::OrdinalIgnoreCase)
    if ($idx -gt 0) {
        return [PSCustomObject]@{
            Exe = $str.Substring(0, $idx + 4).Trim('"', ' ')
            Args = $str.Substring($idx + 4).Trim()
        }
    }
    $parts = $str -split ' ', 2
    return [PSCustomObject]@{
        Exe = $parts[0].Trim('"', ' ')
        Args = if ($parts.Length -gt 1) { $parts[1].Trim() } else { '' }
    }
}

$exitCode = -1

# 1. QuietUninstallString varsa doğrudan çalıştır
if ($quietCmd) {
    $p = Parse-Cmd $quietCmd
    if ($p.Exe -and (Test-Path $p.Exe)) {
        $proc = if ($p.Args) {
            Start-Process -FilePath $p.Exe -ArgumentList $p.Args -Wait -PassThru -NoNewWindow
        } else {
            Start-Process -FilePath $p.Exe -Wait -PassThru -NoNewWindow
        }
        if ($proc) { $exitCode = $proc.ExitCode }
    } else {
        $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$quietCmd`"" -Wait -PassThru -NoNewWindow
        if ($proc) { $exitCode = $proc.ExitCode }
    }
}
# 2. MSI paketleri ({GUID} veya msiexec)
elseif ($rawCmd -match '\{[0-9a-fA-F\-]{36}\}' -or $rawCmd -like '*msiexec*') {
    if ($rawCmd -match '\{[0-9a-fA-F\-]{36}\}') {
        $guid = $matches[0]
        $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/x $guid /qn /norestart" -Wait -PassThru -NoNewWindow
        if ($proc) { $exitCode = $proc.ExitCode }
    } else {
        $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$rawCmd /qn /norestart`"" -Wait -PassThru -NoNewWindow
        if ($proc) { $exitCode = $proc.ExitCode }
    }
}
# 3. Standart EXE kaldırıcılar
elseif ($rawCmd) {
    $p = Parse-Cmd $rawCmd
    $exe = $p.Exe
    $args = $p.Args
    
    if ($args -notmatch '(?i)(/s|/quiet|/qn|/verysilent|/silent|-quiet|-s|-silent)') {
        $name = [System.IO.Path]::GetFileName($exe).ToLowerInvariant()
        if ($name -like '*unins*') {
            $args = "$args /VERYSILENT /SUPPRESSMSGBOXES /NORESTART".Trim()
        } elseif ($exe -like '*Package Cache*' -or $name -like '*redist*') {
            $args = "$args /uninstall /quiet /norestart".Trim()
        } elseif ($name -eq 'uninstall.exe') {
            $args = "$args /S".Trim()
        } else {
            $args = "$args /S /quiet".Trim()
        }
    }
    
    if ($args -like '*--uninstall*' -and $args -notlike '*--force-uninstall*') {
        $args = "$args --force-uninstall".Trim()
    }
    
    if (Test-Path $exe) {
        $proc = if ($args) {
            Start-Process -FilePath $exe -ArgumentList $args -Wait -PassThru -NoNewWindow
        } else {
            Start-Process -FilePath $exe -Wait -PassThru -NoNewWindow
        }
        if ($proc) { $exitCode = $proc.ExitCode }
    } else {
        $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"`"$exe`" $args`"" -Wait -PassThru -NoNewWindow
        if ($proc) { $exitCode = $proc.ExitCode }
    }
}
# 4. Fallback: Registry üzerinden ara
else {
    $item = Get-ItemProperty HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\*, HKLM:\Software\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*, HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\* -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -eq $appName -or $_.DisplayName -like "*$appName*" } | Select-Object -First 1
    if ($item -and $item.QuietUninstallString) {
        $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList "/c `"$($item.QuietUninstallString)`"" -Wait -PassThru -NoNewWindow
        if ($proc) { $exitCode = $proc.ExitCode }
    } elseif ($item -and $item.UninstallString) {
        if ($item.UninstallString -match '\{[0-9a-fA-F\-]{36}\}') {
            $guid = $matches[0]
            $proc = Start-Process -FilePath 'msiexec.exe' -ArgumentList "/x $guid /qn /norestart" -Wait -PassThru -NoNewWindow
            if ($proc) { $exitCode = $proc.ExitCode }
        } else {
            $p = Parse-Cmd $item.UninstallString
            $proc = Start-Process -FilePath $p.Exe -ArgumentList "$($p.Args) /S /quiet".Trim() -Wait -PassThru -NoNewWindow
            if ($proc) { $exitCode = $proc.ExitCode }
        }
    }
}

if ($exitCode -eq 0 -or $exitCode -eq 3010) {
    Write-Output "$appName başarıyla kaldırıldı (Kod: $exitCode)."
    exit 0
} else {
    $msg = if ($exitCode -eq -1) { "$appName için geçerli bir kaldırma dizesi veya kayıt defteri girdisi bulunamadı." } else { "$appName kaldırılamadı. Çıkış Kodu: $exitCode" }
    [Console]::Error.WriteLine($msg)
    Write-Output $msg
    exit (if ($exitCode -gt 0) { $exitCode } else { 1 })
}
""";

            var psBytes = Encoding.Unicode.GetBytes(psCommand);
            var base64 = Convert.ToBase64String(psBytes);
            var cmdToRun = $"powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {base64}";

            var requestId = Guid.NewGuid();
            var initiatorIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? initiatorUserId = Guid.TryParse(initiatorIdStr, out var pId) ? pId : null;
            var initiatorEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
            var corrId = http.GetCorrelationId();

            commandQueue.Enqueue(requestId, id, "uninstall-app", "cmd", cmdToRun, 120, initiatorUserId, initiatorEmail, corrId);
            auditLog.Log(http, "device.uninstall_app", "Device", id.ToString(), new { appName = request.AppName, device.DeviceName });

            if (!device.IsOnline)
            {
                return Results.Json(new
                {
                    success = false,
                    queued = true,
                    appName = request.AppName,
                    exitCode = 202,
                    stdOut = "",
                    stdErr = "Cihaz çevrimdışı. Kaldırma isteği kuyruğa alındı ve cihaz çevrimiçi olduğunda çalıştırılacak.",
                    message = $"{request.AppName} kaldırma isteği kuyruğa alındı."
                }, statusCode: StatusCodes.Status202Accepted);
            }

            var tcs = commandManager.RegisterCommand(requestId, id);
            commandQueue.MarkDelivered(requestId);

            var targetGroup = signalSessionAccess.HasServiceConnection(id)
                ? $"device:{id}:service"
                : $"device:{id}";

            await hubContext.Clients.Group(targetGroup).SendAsync(
                "ExecuteWebCommand", requestId, "cmd", cmdToRun, false, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            try
            {
                using (cts.Token.Register(() => commandManager.CancelCommand(requestId)))
                {
                    var result = await tcs.Task;
                    commandQueue.Complete(id, result);
                    var isSuccess = result.ExitCode == 0 || result.ExitCode == 3010;
                    if (isSuccess)
                    {
                        deviceRegistry.RemoveInstalledApp(id, request.AppName ?? string.Empty);
                    }
                    return Results.Ok(new
                    {
                        success = isSuccess,
                        appName = request.AppName ?? string.Empty,
                        exitCode = result.ExitCode,
                        stdOut = result.StdOut,
                        stdErr = result.StdErr,
                        message = isSuccess 
                            ? $"{request.AppName} uygulaması başarıyla sessizce kaldırıldı." 
                            : $"{request.AppName} kaldırma işlemi başarısız oldu (Çıkış Kodu: {result.ExitCode})."
                    });
                }
            }
            catch (OperationCanceledException)
            {
                commandManager.CancelCommand(requestId);
                commandQueue.MarkTimedOut(requestId, 90);
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

        // Uzaktan Güç ve Oturum Yönetimi (Reboot, Shutdown, Lock, Logoff)
        authed.MapPost("/devices/{id:guid}/power", async (
            Guid id,
            DevicePowerRequest request,
            HttpContext http,
            ClaimsPrincipal user,
            DeviceRegistry devices,
            AuditLogService auditLog,
            IHubContext<SignalingHub> hubContext,
            SignalSessionAccess signalSessionAccess,
            CancellationToken ct) =>
        {
            var device = devices.GetById(id);
            if (device is null)
            {
                return Results.NotFound(new { message = "Cihaz bulunamadı." });
            }

            if (string.IsNullOrWhiteSpace(request.Action))
            {
                return Results.BadRequest(new { message = "Güç eylemi belirtilmelidir." });
            }

            var action = request.Action.Trim().ToLowerInvariant();
            var allowedActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "reboot", "reboot-safe", "reboot-normal", "shutdown", "lock", "logoff"
            };

            if (!allowedActions.Contains(action))
            {
                return Results.BadRequest(new { message = $"Geçersiz güç eylemi '{action}'. Desteklenen eylemler: reboot, shutdown, lock, logoff, reboot-safe, reboot-normal." });
            }

            if (!device.IsOnline)
            {
                return Results.BadRequest(new { message = "Cihaz çevrimdışı olduğu için güç eylemi iletilemedi." });
            }

            var initiatorEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email") ?? "Bilinmeyen";

            auditLog.Log(http, $"device.power.{action}", "Device", id.ToString(), new
            {
                action,
                device.DeviceName,
                device.IpAddress,
                initiatorEmail
            });

            var targetGroup = signalSessionAccess.HasServiceConnection(id)
                ? $"device:{id}:service"
                : $"device:{id}";

            await hubContext.Clients.Group(targetGroup).SendAsync("ExecutePowerAction", action, ct);

            string userFriendlyMessage = action switch
            {
                "reboot" => $"{device.DeviceName} için yeniden başlatma komutu iletildi.",
                "reboot-safe" => $"{device.DeviceName} için güvenli modda yeniden başlatma komutu iletildi.",
                "reboot-normal" => $"{device.DeviceName} için normal modda yeniden başlatma komutu iletildi.",
                "shutdown" => $"{device.DeviceName} için kapatma komutu iletildi.",
                "lock" => $"{device.DeviceName} için ekran kilitleme komutu iletildi.",
                "logoff" => $"{device.DeviceName} için oturum kapatma komutu iletildi.",
                _ => "Güç komutu cihaza iletildi."
            };

            return Results.Ok(new
            {
                success = true,
                action,
                message = userFriendlyMessage
            });
        });

        // Admin Device endpoints
        admin.MapDelete("/devices/{id:guid}", (
            Guid id,
            bool? uninstallAgent,
            HttpContext http,
            ClaimsPrincipal user,
            DeviceRegistry devices,
            DeviceCommandQueue commandQueue,
            AuditLogService auditLog,
            IHubContext<SignalingHub> hub) =>
        {
            var device = devices.GetById(id);
            if (device is null)
            {
                return Results.NotFound(new { message = "Cihaz bulunamadı." });
            }

            var initiatorIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid? initiatorUserId = Guid.TryParse(initiatorIdStr, out var pId) ? pId : null;
            var initiatorEmail = user.FindFirstValue(ClaimTypes.Email) ?? user.FindFirstValue("email");
            var corrId = http.GetCorrelationId();

            if (uninstallAgent ?? true)
            {
                var requestId = Guid.NewGuid();
                commandQueue.Enqueue(requestId, id, "agent-uninstall", "internal", "StartSilentCleanup", 30, initiatorUserId, initiatorEmail, corrId);
                auditLog.Log(http, "device.uninstall_agent", "Device", id.ToString(), new { device.DeviceName });
                return Results.Json(new
                {
                    requestId,
                    queued = true,
                    message = device.IsOnline
                        ? "Ajan kaldırma işi kuyruğa alındı. Cihaz bir sonraki heartbeat döngüsünde işi alacak."
                        : "Cihaz çevrimdışı. Ajan kaldırma işi kuyruğa alındı ve cihaz çevrimiçi olduğunda çalışacak."
                }, statusCode: StatusCodes.Status202Accepted);
            }

            var deleted = devices.Delete(id);
            if (deleted)
            {
                _ = hub.Clients.Group("devices:feed").SendAsync("DeviceDeletedDelta", id);
                auditLog.Log(http, "device.delete", "Device", id.ToString(), new { device.DeviceName });
            }
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
