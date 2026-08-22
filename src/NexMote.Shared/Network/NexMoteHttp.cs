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
    public const string LiveServerIp = "186.241.21.133";
    public const string OldServerIp = "72.62.198.100";

    public static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
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
    /// Windows Servisi ve Tray süreçleri için ORTAK sunucu URL doğrulama kuralı: yerel/özel adreslere
    /// veya localhost'a zorla üretim sunucusuna yönlendirir. Bu iki süreç de yüksek yetkiyle (SYSTEM /
    /// kullanıcı oturumu + SYSTEM input-helper) çalıştığından, ServerUrl ayarının kazara veya kötü niyetle
    /// yerel/sahte bir adrese yönlendirilmesi ciddi bir ele geçirme riskidir — bu yüzden NormalizeUrl'den
    /// (genel amaçlı, localhost'a izin veren) farklı olarak burada localhost dahil tüm özel adresler reddedilir.
    ///
    /// Zorlama kuralları:
    ///  • Boş / null              → üretim URL'sine zorla
    ///  • Şema http:// ise        → üretim URL'sine zorla (TLS zorunlu)
    ///  • Yerel/özel IP aralıkları (127.x, 192.168.x, 10.x, 172.16-31.x) veya "localhost" → üretim URL'sine zorla
    ///  • Geçerli https:// URL    → TrimEnd('/') ile döndür
    /// </summary>
    public static string EnforceProductionUrl(string? rawUrl)
    {
        const string productionUrl = "https://nexmote.com";

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return productionUrl;
        }

        if (rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return productionUrl;
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return productionUrl;
        }

        var host = uri.Host;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return productionUrl;
        }

        if (IsPrivateOrLocalHost(host))
        {
            return productionUrl;
        }

        return rawUrl.TrimEnd('/');
    }

    /// <summary>
    /// Verilen host string'inin RFC-1918 özel IP aralıklarına veya loopback'e ait olup olmadığını kontrol eder.
    /// </summary>
    private static bool IsPrivateOrLocalHost(string host)
    {
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
    /// Kullanıcı veya konfigürasyon tarafından girilen sunucu adresini (örn: "nexmote.com", "www.nexmote.com", "http://nexmote.com")
    /// standart, güvenli ve geçerli bir mutlak HTTPS URL'ine dönüştürür.
    /// Başına https:// veya www. yazılmasa dahi otomatik olarak doğru formata çevirir.
    /// </summary>
    public static string NormalizeUrl(string? rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return "https://nexmote.com";
        }

        var url = rawUrl.Trim().TrimEnd('/');

        // Eğer kullanıcı protokol belirtmediyse (örn: "nexmote.com" veya "www.nexmote.com") https:// ekle
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            url = "https://" + url;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            // Localhost veya yerel IP değilse ve http:// ile girilmişse güvenli https:// protokolüne yükselt
            if (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) &&
                !uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url.Substring(7);
            }

            return url.TrimEnd('/');
        }

        return "https://nexmote.com";
    }
}
