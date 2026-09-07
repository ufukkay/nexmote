using System;
using System.Collections.Generic;

namespace NexMote.Shared.Security;

public sealed record ValidatedDeepLink(
    Guid SessionId,
    string Token,
    string ServerUrl,
    string? DeviceName,
    Guid? DeviceId);

/// <summary>
/// nexmote:// deep-link bağlantı parametrelerini doğrulayan, kötü amaçlı sunucu yönlendirmelerini (serverUrl spoofing)
/// ve geçersiz oturum parametrelerini engelleyen güvenlik doğrulayıcısı.
/// </summary>
public static class DeepLinkValidator
{
    public const string DefaultPublicServer = "https://nexmote.com";

    public static bool TryValidate(
        string? uriString,
        string? trustedServerUrl,
        out ValidatedDeepLink? validated,
        out string? errorMessage,
        out bool requiresHostConfirmation)
    {
        validated = null;
        errorMessage = null;
        requiresHostConfirmation = false;

        if (string.IsNullOrWhiteSpace(uriString))
        {
            errorMessage = "Deep-link URI boş olamaz.";
            return false;
        }

        if (!Uri.TryCreate(uriString, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "nexmote", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Geçersiz URI şeması (yalnızca 'nexmote://' kabul edilir).";
            return false;
        }

        var query = ParseQueryString(uri.Query);

        // 1. Session ID doğrulaması
        if (!query.TryGetValue("sessionId", out var sessionIdStr) ||
            !Guid.TryParse(sessionIdStr, out var sessionId) ||
            sessionId == Guid.Empty)
        {
            errorMessage = "Geçersiz veya eksik 'sessionId' parametresi.";
            return false;
        }

        // 2. Token doğrulaması (minimum 16, maksimum 256 karakter)
        if (!query.TryGetValue("token", out var token) ||
            string.IsNullOrWhiteSpace(token) ||
            token.Length < 16 ||
            token.Length > 256)
        {
            errorMessage = "Geçersiz veya eksik güvenlik 'token' parametresi.";
            return false;
        }

        // 3. Server URL doğrulaması
        string finalServerUrl;
        if (query.TryGetValue("serverUrl", out var serverUrlParam) && !string.IsNullOrWhiteSpace(serverUrlParam))
        {
            if (!Uri.TryCreate(serverUrlParam, UriKind.Absolute, out var serverUri))
            {
                errorMessage = "Geçersiz 'serverUrl' biçimi.";
                return false;
            }

            // Güvenlik: Yalnızca HTTPS veya localhost/127.0.0.1 için HTTP kabul edilir
            var isLocalhost = string.Equals(serverUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(serverUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(serverUri.Scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                !(string.Equals(serverUri.Scheme, "http", StringComparison.OrdinalIgnoreCase) && isLocalhost))
            {
                errorMessage = "Sunucu adresi güvenli HTTPS protokolü kullanmalıdır.";
                return false;
            }

            finalServerUrl = serverUri.GetLeftPart(UriPartial.Authority);
            if (string.Equals(serverUri.Host, "www.nexmote.com", StringComparison.OrdinalIgnoreCase))
            {
                finalServerUrl = "https://nexmote.com";
            }

            // Bilinmeyen üçüncü taraf sunucu kontrolü (nexmote.com ve www.nexmote.com otomatik güvenilir)
            if (!IsTrustedHost(serverUri.Host, trustedServerUrl))
            {
                requiresHostConfirmation = true;
            }
        }
        else
        {
            finalServerUrl = !string.IsNullOrWhiteSpace(trustedServerUrl) ? trustedServerUrl : DefaultPublicServer;
        }

        // 4. İsteğe bağlı cihaz adı ve kimliği
        query.TryGetValue("deviceName", out var deviceName);
        if (!string.IsNullOrWhiteSpace(deviceName) && deviceName.Length > 128)
        {
            deviceName = deviceName[..128];
        }

        Guid? deviceId = null;
        if (query.TryGetValue("deviceId", out var deviceIdStr) && Guid.TryParse(deviceIdStr, out var parsedDevId))
        {
            deviceId = parsedDevId;
        }

        validated = new ValidatedDeepLink(sessionId, token, finalServerUrl, deviceName, deviceId);
        return true;
    }

    private static string? GetHostOrNull(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return uri.Host;
        }
        return null;
    }

    private static string StripWww(string host)
    {
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? host[4..]
            : host;
    }

    private static bool IsTrustedHost(string host, string? trustedServerUrl)
    {
        var cleanHost = StripWww(host);

        // Localhost kontrolü
        if (string.Equals(cleanHost, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cleanHost, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // NexMote resmi alan adları (nexmote.com, www.nexmote.com, *.nexmote.com)
        if (string.Equals(cleanHost, "nexmote.com", StringComparison.OrdinalIgnoreCase) ||
            cleanHost.EndsWith(".nexmote.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Teknisyen uygulamasında ayarlı olan güvenilen sunucu
        var trustedHost = GetHostOrNull(trustedServerUrl);
        if (trustedHost != null)
        {
            var cleanTrusted = StripWww(trustedHost);
            if (string.Equals(cleanHost, cleanTrusted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, string> ParseQueryString(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var trimmed = query.TrimStart('?');
        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);

        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
            }
            else if (parts.Length == 1)
            {
                result[Uri.UnescapeDataString(parts[0])] = string.Empty;
            }
        }

        return result;
    }
}
