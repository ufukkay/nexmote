namespace NexMote.Api.Auth;

/// <summary>
/// Teknisyen ve web konsolu üzerinden gelen isteklerin yetkilendirilmesini sağlayan filtre.
/// "Authorization: Bearer {Admin:ApiKey}" başlığını zorunlu kılar.
/// Agent istekleri (enroll, heartbeat, audit) cihaz bazlı özel token kullandığından bu gruba dahil değildir.
/// </summary>
public sealed class AdminAuthFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedKey = config["Admin:ApiKey"];

        if (string.IsNullOrEmpty(expectedKey))
        {
            return Results.Problem("Sunucuda Admin API anahtarı yapılandırılmamış.", statusCode: StatusCodes.Status500InternalServerError);
        }

        var header = context.HttpContext.Request.Headers.Authorization.ToString();
        var providedKey = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : string.Empty;

        if (string.IsNullOrEmpty(providedKey) || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
