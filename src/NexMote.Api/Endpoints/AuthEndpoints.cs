using System.Security.Claims;
using NexMote.Api.Data;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

namespace NexMote.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app, RouteGroupBuilder authed, RouteGroupBuilder admin)
    {
        // Public Auth
        app.MapPost("/api/auth/login", (AdminLoginRequest request, bool? rememberMe, HttpContext http, UserAuthService auth) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var response = auth.LoginStep1(request.Email, request.Password, rememberMe ?? false, ip, http.Request.Headers.UserAgent.ToString());
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/mfa/verify", (MfaVerifyRequest request, bool? rememberMe, HttpContext http, UserAuthService auth) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var response = auth.VerifyMfaStep2(request.ChallengeToken, request.Code, rememberMe ?? false, ip, http.Request.Headers.UserAgent.ToString());
            return response is null ? Results.Unauthorized() : Results.Ok(response);
        }).RequireRateLimiting("login");

        app.MapGet("/api/invite/{token}", (string token, UserAuthService auth) =>
        {
            var preview = auth.GetInvitePreview(token);
            return preview is null
                ? Results.NotFound(new { message = "Davet geçersiz, süresi dolmuş veya zaten kullanılmış." })
                : Results.Ok(new InvitePreviewResponse(preview.Value.Email, preview.Value.DisplayName, preview.Value.Role));
        }).RequireRateLimiting("login");

        app.MapPost("/api/invite/{token}/accept", (string token, AcceptInviteRequest request, HttpContext http, UserAuthService auth) =>
        {
            var ip = http.Connection.RemoteIpAddress?.ToString();
            var response = auth.AcceptInvite(token, request.Password, ip, http.Request.Headers.UserAgent.ToString());
            return response is null
                ? Results.BadRequest(new { message = "Davet geçersiz, süresi dolmuş veya zaten kullanılmış." })
                : Results.Ok(response);
        }).RequireRateLimiting("login");

        app.MapPost("/api/auth/logout", (HttpContext http, UserAuthService auth) =>
        {
            var header = http.Request.Headers.Authorization.ToString();
            if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                auth.Logout(header["Bearer ".Length..].Trim(), http.Connection.RemoteIpAddress?.ToString());
            }
            return Results.NoContent();
        }).RequireAuthorization("AnyUser");

        app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
        {
            return Results.Ok(new CurrentUserResponse(
                Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
                user.FindFirstValue(ClaimTypes.Email)!,
                user.FindFirstValue("display_name") ?? user.FindFirstValue(ClaimTypes.Email)!,
                user.FindFirstValue(ClaimTypes.Role)!,
                MfaEnabled: user.FindFirstValue("mfa_enabled") == "true"));
        }).RequireAuthorization("AnyUser");

        // Authed Account Endpoints
        authed.MapPost("/account/password", (ChangePasswordRequest request, ClaimsPrincipal user, UserAuthService auth) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return auth.ChangePassword(userId, request.CurrentPassword, request.NewPassword)
                ? Results.NoContent()
                : Results.BadRequest(new { message = "Mevcut şifre hatalı." });
        });

        authed.MapPost("/account/mfa/setup", (ClaimsPrincipal user, UserAuthService auth) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Results.Ok(auth.SetupMfa(userId));
        });

        authed.MapPost("/account/mfa/enable", (MfaEnableRequest request, ClaimsPrincipal user, UserAuthService auth) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = auth.EnableMfa(userId, request.Code);
            return result is null ? Results.BadRequest(new { message = "Kod doğrulanamadı." }) : Results.Ok(result);
        });

        authed.MapPost("/account/mfa/disable", (MfaDisableRequest request, ClaimsPrincipal user, UserAuthService auth) =>
        {
            var userId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return auth.DisableMfa(userId, request.CurrentPassword)
                ? Results.NoContent()
                : Results.BadRequest(new { message = "Şifre hatalı." });
        });

        // Admin User Management Endpoints
        admin.MapGet("/admin/users", (UserAuthService auth) => Results.Ok(auth.ListUsers()));

        admin.MapPost("/admin/users", (CreateUserRequest request, ClaimsPrincipal actor, UserAuthService auth) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = auth.CreateUser(request.Email, request.DisplayName, request.Role, actingUserId);
            return result is null ? Results.BadRequest(new { message = "E-posta zaten kayıtlı veya rol geçersiz." }) : Results.Ok(result);
        });

        admin.MapPost("/admin/users/invite", async (InviteUserRequest request, ClaimsPrincipal actor, UserAuthService auth, EmailService email, IConfiguration config) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var result = auth.InviteUser(request.Email, request.DisplayName, request.Role, actingUserId);
            if (result is null)
            {
                return Results.BadRequest(new { message = "E-posta zaten kayıtlı (ve daveti kabul edilmiş) ya da rol geçersiz." });
            }

            var baseUrl = config["PublicUrl"] ?? "https://nexmote.com";
            var inviteUrl = $"{baseUrl.TrimEnd('/')}/invite/{result.Value.Token}";
            var roleLabel = result.Value.Role == UserRoles.Admin ? "Admin" : "Teknisyen";
            var (success, error) = await email.SendAsync(
                result.Value.Email,
                "NexMote'a Davet Edildiniz",
                $"""
                <p>Merhaba {System.Net.WebUtility.HtmlEncode(result.Value.DisplayName)},</p>
                <p>NexMote uzaktan yönetim konsoluna <strong>{roleLabel}</strong> yetkisiyle davet edildiniz.</p>
                <p>Hesabınızı etkinleştirmek ve kendi şifrenizi belirlemek için aşağıdaki bağlantıya tıklayın (7 gün geçerlidir):</p>
                <p><a href="{inviteUrl}">{inviteUrl}</a></p>
                """);

            if (!success)
            {
                return Results.BadRequest(new { message = error });
            }

            return Results.Ok(new { message = "Davet e-postası gönderildi.", email = result.Value.Email });
        });

        admin.MapPost("/admin/users/{id:guid}/role", (Guid id, SetRoleRequest request, ClaimsPrincipal actor, UserAuthService auth) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return auth.SetRole(id, request.Role, actingUserId) ? Results.NoContent() : Results.BadRequest(new { message = "Rol geçersiz veya kullanıcı bulunamadı." });
        });

        admin.MapPost("/admin/users/{id:guid}/disable", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (id == actingUserId)
            {
                return Results.BadRequest(new { message = "Kendi hesabınızı devre dışı bırakamazsınız." });
            }
            return auth.SetActive(id, false, actingUserId) ? Results.NoContent() : Results.NotFound();
        });

        admin.MapPost("/admin/users/{id:guid}/enable", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return auth.SetActive(id, true, actingUserId) ? Results.NoContent() : Results.NotFound();
        });

        admin.MapPost("/admin/users/{id:guid}/mfa/reset", (Guid id, ClaimsPrincipal actor, UserAuthService auth) =>
        {
            var actingUserId = Guid.Parse(actor.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return auth.AdminResetMfa(id, actingUserId) ? Results.NoContent() : Results.NotFound();
        });

        admin.MapGet("/admin/audit-log", (int? page, int? pageSize, Guid? userId, string? action, UserAuthService auth) =>
        {
            var (items, total) = auth.GetAuditLog(page ?? 1, pageSize ?? 50, userId, action);
            return Results.Ok(new { items, total });
        });
    }
}
