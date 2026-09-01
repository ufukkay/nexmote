using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class SecurityProfileEndpoints
{
    public static void MapSecurityProfileEndpoints(this WebApplication app, RouteGroupBuilder authed, RouteGroupBuilder admin)
    {
        // Public / Agent endpoints
        app.MapGet("/api/agents/{deviceId:guid}/security-profile", (Guid deviceId, string agentToken, SecurityProfileService profiles) =>
        {
            var result = profiles.GetAgentProfile(deviceId, agentToken);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        }).RequireRateLimiting("agent");

        app.MapPost("/api/agents/{deviceId:guid}/security/verify", (Guid deviceId, SecurityVerifyRequest request, SecurityProfileService profiles) =>
        {
            var ok = profiles.VerifyPassword(deviceId, request.AgentToken, request.Action, request.Password);
            return Results.Ok(new SecurityVerifyResponse(ok));
        }).RequireRateLimiting("agent");

        // Authed user catalog read
        authed.MapGet("/security-profiles", (SecurityProfileService profiles) => Results.Ok(profiles.List()));

        // Admin Security Profile Management
        admin.MapGet("/admin/security-profiles", (SecurityProfileService profiles) => Results.Ok(profiles.List()));

        admin.MapPost("/admin/security-profiles", (SecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = profiles.Create(request, actingUserId);
            return result is null ? Results.BadRequest(new { message = "Ad boş olamaz veya zorunlu bir şifre eksik." }) : Results.Ok(result);
        });

        admin.MapPut("/admin/security-profiles/{id:guid}", async (Guid id, SecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = profiles.Update(id, request, actingUserId);
            if (result is not null)
            {
                await hub.Clients.All.SendAsync("SecurityProfileUpdated");
            }
            return result is null ? Results.BadRequest(new { message = "Profil bulunamadı, ad boş olamaz veya zorunlu bir şifre eksik." }) : Results.Ok(result);
        });

        admin.MapDelete("/admin/security-profiles/{id:guid}", async (Guid id, ClaimsPrincipal actor, SecurityProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = profiles.Delete(id, actingUserId);
            if (success)
            {
                await hub.Clients.All.SendAsync("SecurityProfileUpdated");
            }
            return success ? Results.NoContent() : Results.NotFound();
        });

        admin.MapPost("/devices/{id:guid}/security-profile", async (Guid id, AssignSecurityProfileRequest request, ClaimsPrincipal actor, SecurityProfileService profiles, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = profiles.AssignToDevice(id, request.SecurityProfileId, actingUserId);
            if (success)
            {
                await hub.Clients.Group($"device:{id}").SendAsync("SecurityProfileUpdated");
            }
            return success ? Results.NoContent() : Results.NotFound();
        });
    }
}
