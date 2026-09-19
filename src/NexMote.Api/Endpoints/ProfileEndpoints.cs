using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NexMote.Api.Data;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app, RouteGroupBuilder authed, RouteGroupBuilder admin)
    {
        // ----------------------------------------------------------------- Agent Endpoints (Cihaz tarafı)

        app.MapGet("/api/agents/{deviceId:guid}/policy", (Guid deviceId, string agentToken, ProfileService profiles, AppDbContext db) =>
        {
            var device = db.Devices.AsNoTracking().FirstOrDefault(d => d.Id == deviceId);
            if (device == null || !string.Equals(device.AgentToken, agentToken, StringComparison.Ordinal))
            {
                return Results.Unauthorized();
            }

            var policy = profiles.GetEffectivePolicyForDevice(deviceId);
            return Results.Ok(policy);
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/policy-ack", (Guid deviceId, string agentToken, int appliedVersion, ProfileService profiles) =>
        {
            var ok = profiles.AcknowledgePolicy(deviceId, agentToken, appliedVersion);
            return ok ? Results.NoContent() : Results.Unauthorized();
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/verify-protection", (Guid deviceId, VerifyProtectionRequest request, ProfileService profiles) =>
        {
            var ok = profiles.VerifyProtectionPassword(deviceId, request.AgentToken, request.Action, request.Password);
            return Results.Ok(new VerifyProtectionResponse(ok, ok ? null : "Geçersiz koruma şifresi."));
        }).RequireRateLimiting("agent");

        // ----------------------------------------------------------------- Web Konsolu (Authed / Admin)

        authed.MapGet("/profiles/tree", (ProfileService profiles) => Results.Ok(profiles.GetTree()));

        authed.MapGet("/profiles/{id:guid}", (Guid id, ProfileService profiles) =>
        {
            var result = profiles.Get(id);
            return result != null ? Results.Ok(result) : Results.NotFound();
        });

        admin.MapPost("/admin/profiles", (ProfileUpsertRequest request, ClaimsPrincipal actor, ProfileService profiles) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = profiles.Create(request, actingUserId);
            return result != null ? Results.Ok(result) : Results.BadRequest(new { message = "Profil adı boş olamaz veya geçersiz üst profil." });
        });

        admin.MapPut("/admin/profiles/{id:guid}", async (Guid id, ProfileUpsertRequest request, ClaimsPrincipal actor, ProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = profiles.Update(id, request, actingUserId);
            if (result != null)
            {
                await hub.Clients.All.SendAsync("PolicyUpdated", id);
            }
            return result != null ? Results.Ok(result) : Results.BadRequest(new { message = "Profil güncellenemedi." });
        });

        admin.MapDelete("/admin/profiles/{id:guid}", (Guid id, ClaimsPrincipal actor, ProfileService profiles) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = profiles.Delete(id, actingUserId, out var error);
            return success ? Results.NoContent() : Results.BadRequest(new { message = error ?? "Silme işlemi başarısız." });
        });

        admin.MapPost("/admin/profiles/{id:guid}/clone", (Guid id, string? newName, ClaimsPrincipal actor, ProfileService profiles) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = profiles.Clone(id, newName ?? string.Empty, actingUserId);
            return result != null ? Results.Ok(result) : Results.NotFound();
        });

        admin.MapPost("/admin/profiles/{id:guid}/apply-now", async (Guid id, ClaimsPrincipal actor, IHubContext<SignalingHub> hub, AppDbContext db) =>
        {
            // Bu profile bağlı tüm cihazlara SignalR üzerinden "PolicySyncRequired" sinyali fırlat
            var deviceIds = db.Devices.Where(d => d.ProfileId == id).Select(d => d.Id).ToList();
            foreach (var devId in deviceIds)
            {
                await hub.Clients.Group($"device:{devId}").SendAsync("PolicySyncRequired");
            }
            return Results.Ok(new { message = $"{deviceIds.Count} adet cihaza anlık politika uygulama sinyali gönderildi." });
        });

        admin.MapPost("/admin/devices/bulk-assign-profile", async (BulkAssignProfileRequest request, ClaimsPrincipal actor, ProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var count = profiles.BulkAssignDevices(request.DeviceIds, request.TargetProfileId, actingUserId);
            foreach (var devId in request.DeviceIds)
            {
                await hub.Clients.Group($"device:{devId}").SendAsync("PolicySyncRequired");
            }
            return Results.Ok(new { count });
        });

        admin.MapPost("/devices/{id:guid}/profile", async (Guid id, Guid? profileId, ClaimsPrincipal actor, ProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = profiles.AssignDevice(id, profileId, actingUserId);
            if (success)
            {
                await hub.Clients.Group($"device:{id}").SendAsync("PolicySyncRequired");
            }
            return success ? Results.NoContent() : Results.NotFound();
        });

        admin.MapPut("/devices/{id:guid}/override-policy", async (Guid id, DevicePolicyOverrideRequest request, ClaimsPrincipal actor, ProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = profiles.SetDeviceOverride(id, request, actingUserId);
            if (success)
            {
                await hub.Clients.Group($"device:{id}").SendAsync("PolicySyncRequired");
            }
            return success ? Results.NoContent() : Results.NotFound();
        });

        authed.MapGet("/devices/{id:guid}/effective-policy", (Guid id, ProfileService profiles) =>
        {
            var policy = profiles.GetEffectivePolicyForDevice(id);
            return Results.Ok(policy);
        });
    }
}
