namespace NexMote.Api.Auth;

public static class SessionCookie
{
    public const string Name = "nexmote_session";

    public static void Set(HttpContext http, string token, bool rememberMe)
    {
        var options = BuildOptions(http);
        if (rememberMe)
        {
            options.Expires = DateTimeOffset.UtcNow.AddDays(30);
        }

        http.Response.Cookies.Append(Name, token, options);
    }

    public static string? Read(HttpContext http) => ReadWithSource(http).Token;

    public static (string? Token, bool FromCookie) ReadWithSource(HttpContext http)
    {
        var header = http.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (header["Bearer ".Length..].Trim(), false);
        }

        if (http.Request.Cookies.TryGetValue(Name, out var cookieToken) && !string.IsNullOrWhiteSpace(cookieToken))
        {
            return (cookieToken, true);
        }

        if (http.Request.Query.TryGetValue("access_token", out var queryToken) && !string.IsNullOrWhiteSpace(queryToken))
        {
            return (queryToken.ToString(), false);
        }

        return (null, false);
    }

    public static void Clear(HttpContext http) =>
        http.Response.Cookies.Delete(Name, BuildOptions(http));

    private static CookieOptions BuildOptions(HttpContext http) => new()
    {
        HttpOnly = true,
        Secure = http.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = "/"
    };
}
