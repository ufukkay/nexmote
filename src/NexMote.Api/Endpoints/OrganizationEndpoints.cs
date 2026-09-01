using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using NexMote.Api.Hubs;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static void MapOrganizationEndpoints(this RouteGroupBuilder admin)
    {
        admin.MapGet("/admin/device-groups", (DeviceGroupService groups) => Results.Ok(groups.List()));

        admin.MapPost("/admin/device-groups", (DeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = groups.Create(request, actingUserId);
            return result is null ? Results.BadRequest(new { message = "Ad boş olamaz veya üst grup/profil geçersiz." }) : Results.Ok(result);
        });

        admin.MapPut("/admin/device-groups/{id:guid}", async (Guid id, DeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = groups.Update(id, request, actingUserId);
            if (result is not null)
            {
                await hub.Clients.All.SendAsync("SecurityProfileUpdated");
            }
            return result is null ? Results.BadRequest(new { message = "Grup bulunamadı, ad boş olamaz, üst grup bir çevrim oluşturuyor ya da profil geçersiz." }) : Results.Ok(result);
        });

        admin.MapDelete("/admin/device-groups/{id:guid}", async (Guid id, ClaimsPrincipal actor, DeviceGroupService groups, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var (success, error) = groups.Delete(id, actingUserId);
            if (success)
            {
                await hub.Clients.All.SendAsync("SecurityProfileUpdated");
            }
            return success ? Results.NoContent() : Results.BadRequest(new { message = error });
        });

        admin.MapPost("/devices/{id:guid}/group", async (Guid id, AssignDeviceGroupRequest request, ClaimsPrincipal actor, DeviceGroupService groups, IHubContext<SignalingHub> hub) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var success = groups.AssignDeviceToGroup(id, request.GroupId, actingUserId);
            if (success)
            {
                await hub.Clients.Group($"device:{id}").SendAsync("SecurityProfileUpdated");
            }
            return success ? Results.NoContent() : Results.NotFound();
        });

        admin.MapPost("/admin/device-groups/{id:guid}/enrollment-key/regenerate", (Guid id, ClaimsPrincipal actor, DeviceGroupService groups) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = groups.RegenerateEnrollmentKey(id, actingUserId);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });

        admin.MapGet("/admin/device-groups/{id:guid}/provision-script", (Guid id, string? serverUrl, DeviceGroupService groups, IConfiguration config) =>
        {
            var effectiveServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? (config["Server:PublicUrl"] ?? "https://nexmote.com") : serverUrl;
            var result = groups.BuildProvisionScript(id, effectiveServerUrl);
            if (result is null)
            {
                return Results.NotFound(new { message = "Grup bulunamadı veya kurulum anahtarı yok." });
            }

            var safeName = string.Concat(result.Value.GroupName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            if (string.IsNullOrEmpty(safeName)) safeName = "Grup";
            var bytes = System.Text.Encoding.UTF8.GetBytes(result.Value.Script);
            return Results.File(bytes, "text/plain", $"NexMote-Provision-{safeName}.ps1");
        });

        admin.MapGet("/admin/device-groups/{id:guid}/install-script", (Guid id, string? serverUrl, DeviceGroupService groups, IConfiguration config) =>
        {
            var effectiveServerUrl = string.IsNullOrWhiteSpace(serverUrl) ? (config["Server:PublicUrl"] ?? "https://nexmote.com") : serverUrl;
            var result = groups.BuildInstallScript(id, effectiveServerUrl);
            if (result is null)
            {
                return Results.NotFound(new { message = "Grup bulunamadı veya kurulum anahtarı yok." });
            }

            var safeName = string.Concat(result.Value.GroupName.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_'));
            if (string.IsNullOrEmpty(safeName)) safeName = "Grup";
            var bytes = System.Text.Encoding.UTF8.GetBytes(result.Value.Script);
            return Results.File(bytes, "text/plain", $"NexMote-Install-{safeName}.ps1");
        });
    }
}
