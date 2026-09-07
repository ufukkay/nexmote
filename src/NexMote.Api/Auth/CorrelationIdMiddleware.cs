namespace NexMote.Api.Auth;

/// <summary>
/// Gelen HTTP isteklerinde X-Correlation-Id başlığını okuyan veya yeni bir izleme kimliği üreterek
/// HttpContext.TraceIdentifier ve yanıt başlıklarına ekleyen middleware.
/// Tüm sistem aktivitelerini ve SignalR/DB denetim kayıtlarını uçtan uca ilişkilendirir.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate _next)
    {
        this._next = _next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.TraceIdentifier = correlationId;
        context.Items["CorrelationId"] = correlationId;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
            {
                context.Response.Headers.Append(HeaderName, correlationId);
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class CorrelationIdExtensions
{
    public static string GetCorrelationId(this HttpContext context)
    {
        if (context.Items.TryGetValue("CorrelationId", out var id) && id is string s && !string.IsNullOrWhiteSpace(s))
        {
            return s;
        }

        return context.TraceIdentifier;
    }
}
