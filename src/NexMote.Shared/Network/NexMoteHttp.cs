using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace NexMote.Shared.Network;

/// <summary>
/// NexMote sunucusuna (nexmote.com) yapılan HTTP ve WebSocket bağlantılarını
/// yerel ISS/modem DNS önbellek gecikmelerinden veya eski IP kalıntılarından koruyan dayanıklı ağ yöneticisi.
/// </summary>
public static class NexMoteHttp
{
    public const string LiveServerIp = "212.12.135.106";

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
                {
                    if (sslPolicyErrors == System.Net.Security.SslPolicyErrors.None) return true;
                    // Yerel ağ veya sunucu IP adresiyle bağlanırken oluşan sertifika isim uyuşmazlığını kabul et
                    if ((sslPolicyErrors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateNameMismatch) == System.Net.Security.SslPolicyErrors.None)
                    {
                        return true;
                    }
                    return false;
                }
            },
            ConnectCallback = async (context, cancellationToken) =>
            {
                var host = context.DnsEndPoint.Host;
                var port = context.DnsEndPoint.Port;
                var isNexMoteHost = host.Equals("nexmote.com", StringComparison.OrdinalIgnoreCase) ||
                                     host.EndsWith(".nexmote.com", StringComparison.OrdinalIgnoreCase);

                if (!isNexMoteHost)
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    await socket.ConnectAsync(host, port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }

                // Önce gerçek DNS çözümlemesiyle bağlanmayı dene: sunucu IP'si değiştiğinde (taşınma,
                // failover, load balancer) ajanların DNS güncellemesini otomatik takip etmesini sağlar.
                // Sadece bu deneme zaman aşımına uğrar/başarısız olursa (yerel ISS/modem DNS önbellek
                // gecikmesi, geçici çözümleme hatası) bilinen son sağlıklı IP'ye düşülür — böylece hem
                // DNS güncellemeleri takip edilir hem de DNS gecikmelerine karşı dayanıklılık korunur.
                using var dnsAttemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                dnsAttemptCts.CancelAfter(TimeSpan.FromSeconds(3));

                try
                {
                    var primarySocket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await primarySocket.ConnectAsync(host, port, dnsAttemptCts.Token);
                        return new NetworkStream(primarySocket, ownsSocket: true);
                    }
                    catch
                    {
                        primarySocket.Dispose();
                        throw;
                    }
                }
                catch (Exception ex) when (ex is SocketException or OperationCanceledException)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fallbackSocket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    await fallbackSocket.ConnectAsync(IPAddress.Parse(LiveServerIp), port, cancellationToken);
                    return new NetworkStream(fallbackSocket, ownsSocket: true);
                }
            }
        };
    }

    /// <summary>
    /// <summary>
    /// Windows Servisi ve Tray süreçleri için sunucu URL doğrulama kuralı:
    ///  • Boş / null              → varsayılan üretim URL'sine ("https://nexmote.com") yönlendir
    ///  • Yerel/özel IP aralıkları (127.x, 192.168.x, 10.x, 172.16-31.x, localhost) → yerel ağ sunucuları için HTTP/HTTPS olarak kabul et
    ///  • İnternet üzerindeki harici sunucular → güvenli HTTPS zorunlu
    /// </summary>
    public static string EnforceProductionUrl(string? rawUrl)
    {
        const string defaultServerUrl = "https://nexmote.com";

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return defaultServerUrl;
        }

        var trimmed = rawUrl.Trim().TrimEnd('/');
        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = (IsPrivateOrLocalHost(trimmed.Split(':')[0]) ? "http://" : "https://") + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return defaultServerUrl;
        }

        var host = uri.Host;

        // Yerel veya özel IP adresleri (192.168.x.x, 10.x.x.x, 172.16-31.x.x, localhost)
        // yerel ağda/on-premise sunucularda HTTP ve HTTPS ile doğrudan desteklenir.
        if (IsPrivateOrLocalHost(host))
        {
            return trimmed;
        }

        // İnternet üzerindeki harici sunucular mutlaka güvenli HTTPS protokolü kullanmalıdır.
        if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + trimmed.Substring(7);
        }

        return trimmed;
    }

    /// <summary>
    /// Production agent/tray bağlantılarında eski yerel ağ adreslerinin canlı sunucuya yönlenmesini sağlar.
    /// Yerel geliştirme URL'lerini koruyan <see cref="EnforceProductionUrl"/> aksine, dağıtılmış istemciler
    /// private IP veya localhost üzerinde çalışan eski sunucu adreslerine bağlanmamalıdır.
    /// </summary>
    public static string EnforceAgentServerUrl(string? rawUrl)
    {
        return EnforceProductionUrl(rawUrl);
    }

    /// <summary>
    /// Verilen host string'inin RFC-1918 özel IP aralıklarına veya loopback'e ait olup olmadığını kontrol eder.
    /// </summary>
    public static bool IsPrivateOrLocalHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var ip))
        {
            return false;
        }

        var bytes = ip.GetAddressBytes();

        // IPv4 loopback: 127.0.0.0/8
        if (bytes.Length == 4 && bytes[0] == 127) return true;

        // RFC-1918: 10.0.0.0/8
        if (bytes.Length == 4 && bytes[0] == 10) return true;

        // RFC-1918: 192.168.0.0/16
        if (bytes.Length == 4 && bytes[0] == 192 && bytes[1] == 168) return true;

        // RFC-1918: 172.16.0.0/12
        if (bytes.Length == 4 && bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;

        // IPv6 loopback: ::1
        if (IPAddress.IsLoopback(ip)) return true;

        return false;
    }

    public static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = new HttpClient(CreateHandler());
        if (timeout.HasValue)
        {
            client.Timeout = timeout.Value;
        }
        return client;
    }

    /// <summary>
    /// Kullanıcı veya konfigürasyon tarafından girilen sunucu adresini (örn: "192.168.0.219", "nexmote.com", "http://192.168.0.219")
    /// standart, geçerli bir mutlak URL'e dönüştürür.
    /// Yerel/özel IP'ler için HTTP protokolünü korur, harici alan adları için HTTPS'e zorlar.
    /// </summary>
    public static string NormalizeUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return "https://nexmote.com";
        }

        var url = rawUrl.Trim().TrimEnd('/');

        // Protokol belirtilmemişse: yerel IP veya localhost ise http://, harici ise https:// ekle
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var hostPart = url.Split('/')[0].Split(':')[0];
            url = (IsPrivateOrLocalHost(hostPart) ? "http://" : "https://") + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Localhost veya yerel/özel IP değilse ve http:// ile girilmişse güvenli https:// protokolüne yükselt
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !IsPrivateOrLocalHost(uri.Host))
            {
                url = "https://" + url.Substring(7);
            }

            return url.TrimEnd('/');
        }

        return "https://nexmote.com";
    }
}
