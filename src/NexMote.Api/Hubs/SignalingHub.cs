using Microsoft.AspNetCore.SignalR;
using NexMote.Api.Services;

namespace NexMote.Api.Hubs;

/// <summary>
/// Teknisyen uygulaması ile hedef Windows Agent arasındaki gerçek zamanlı WebSocket sinyalleşmesini yöneten SignalR Hub'ı.
/// Ekran görüntü kareleri, fare/klavye girdileri, uzak komutlar ve dosya aktarımı bu hub üzerinden odaya (session) aktarılır.
/// </summary>
public sealed class SignalingHub : Hub
{
    private readonly RemoteSessionRegistry _sessions;
    private readonly DeviceRegistry _devices;
    private readonly SignalSessionAccess _access;
    private readonly DeviceCommandManager _commandManager;

    /// <summary>
    /// Mesaj tipine göre maksimum payload boyutu (byte).
    /// Ekran karesi ve dosya transferi büyük olabilir; kontrol/komut mesajları küçük tutulur.
    /// </summary>
    private static readonly Dictionary<string, int> PayloadLimits = new(StringComparer.OrdinalIgnoreCase)
    {
        // Kontrol sinyalleri — küçük JSON nesneleri
        ["screen-info"]        =    8_192,  //  8 KB  — monitör listesi
        ["remote-input"]       =    4_096,  //  4 KB  — fare/klavye olayı
        ["remote-command"]     =   16_384,  // 16 KB  — komut metni (uzun script'lere yer bırak)
        ["command-result"]     =  262_144,  //256 KB  — komut çıktısı (verbose output için)
        ["power-action"]       =    1_024,  //  1 KB  — kapat/yeniden başlat/kilitle
        ["refresh-screen"]     =      256,  //256 B   — boş tetikleyici
        ["ping"]               =      256,
        ["pong"]               =      256,
        ["input-ack"]          =    1_024,
        ["frame-ack"]          =    2_048,
        ["network-probe"]      =    2_048,
        ["network-probe-ack"]  =    2_048,
        ["set-quality-mode"]   =      256,
        ["clipboard-text"]     =   65_536,  // 64 KB  — pano içeriği
        // Büyük yük — ekran karesi ve dosya transferi
        ["screen-frame-multi"] = 3_145_728, //  3 MB  — JPEG frame(ler)
        ["file-chunk"]         = 3_145_728, //  3 MB  — dosya bloğu
    };

    /// <summary>Tanımlı limit yoksa kullanılacak varsayılan güvenli sınır (64 KB).</summary>
    private const int DefaultPayloadLimit = 65_536;

    public SignalingHub(
        RemoteSessionRegistry sessions,
        DeviceRegistry devices,
        SignalSessionAccess access,
        DeviceCommandManager commandManager)
    {
        _sessions = sessions;
        _devices = devices;
        _access = access;
        _commandManager = commandManager;
    }

    /// <summary>
    /// Teknisyen masaüstü uygulamasının geçerli bir oturum token'ı ile canlı oturum odasına katılması.
    /// Başarılı katılımda hedef cihaza "RemoteSessionRequested" sinyali gönderilir.
    /// </summary>
    /// <param name="sessionId">Teknisyen oturum kimliği.</param>
    /// <param name="token">Oturuma özel tek kullanımlık güvenlik token'ı.</param>
    public async Task JoinTechnicianSession(Guid sessionId, string token)
    {
        var session = _sessions.Activate(sessionId, token);
        if (session is null)
        {
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _access.Add(Context.ConnectionId, sessionId, SignalSessionRole.Technician);
        // Hedef cihazın kalıcı sinyal kanalına oturum başlatma isteği ilet
        await Clients.Group($"device:{session.DeviceId}").SendAsync("RemoteSessionRequested", sessionId);
    }

    /// <summary>
    /// Hedef makinedeki Agent'ın sunucuya sürekli açık tuttuğu arka plan dinleme kanalına bağlanması.
    /// Uzaktan oturum açma veya güncelleme bildirimleri bu gruba iletilir.
    /// </summary>
    /// <param name="deviceId">Cihazın benzersiz kimliği.</param>
    /// <param name="agentToken">Cihazın kimlik doğrulama token'ı.</param>
    public async Task JoinDevice(Guid deviceId, string agentToken)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            throw new HubException("Geçersiz cihaz token'ı.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
    }

    /// <summary>
    /// Hedef makinedeki Agent'ın belirli bir aktif teknisyen oturum odasına katılması.
    /// Katılım sağlandığında teknisyene "DeviceJoinedSession" bildirimi iletilir.
    /// </summary>
    /// <param name="sessionId">Aktif oturum kimliği.</param>
    /// <param name="deviceId">Cihaz kimliği.</param>
    /// <param name="agentToken">Cihaz token'ı.</param>
    public async Task JoinDeviceSession(Guid sessionId, Guid deviceId, string agentToken)
    {
        var session = _sessions.Get(sessionId);
        if (session is null || session.DeviceId != deviceId || !_devices.ValidateAgent(deviceId, agentToken))
        {
            throw new HubException("Geçersiz veya süresi dolmuş cihaz oturumu.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _access.Add(Context.ConnectionId, sessionId, SignalSessionRole.Agent);
        await Clients.Group($"session:{sessionId}").SendAsync("DeviceJoinedSession");
    }

    /// <summary>
    /// Oturum odasındaki diğer tarafa (Teknisyen -> Agent veya Agent -> Teknisyen) sinyal mesajı iletme.
    /// Mesaj türleri: screen-info, screen-frame-multi, remote-input, remote-command, command-result, file-chunk vb.
    /// Her mesaj tipine özgü payload boyut sınırı uygulanır; aşım HubException ile reddedilir.
    /// </summary>
    /// <param name="sessionId">Oturum kimliği.</param>
    /// <param name="type">Sinyal türü.</param>
    /// <param name="payload">Sinyalin JSON veri gövdesi.</param>
    public async Task SendSignal(Guid sessionId, string type, string payload)
    {
        if (!_access.Has(Context.ConnectionId, sessionId) || _sessions.Get(sessionId) is null)
        {
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        if (string.IsNullOrWhiteSpace(type) || payload is null)
        {
            throw new HubException("Sinyal türü ve veri gövdesi gereklidir.");
        }

        // Mesaj tipine göre diferansiyel payload boyut sınırı uygula
        var limit = PayloadLimits.TryGetValue(type, out var configured) ? configured : DefaultPayloadLimit;
        if (payload.Length > limit)
        {
            throw new HubException(
                $"'{type}' sinyali izin verilen maksimum boyutu ({limit:N0} karakter) aşıyor. " +
                $"Gelen: {payload.Length:N0} karakter.");
        }

        // Mesajı oturumdaki diğer istemcilere yayınla
        await Clients.OthersInGroup($"session:{sessionId}").SendAsync("SignalReceived", type, payload);
    }

    /// <summary>
    /// Ajan tarafından doğrudan web konsolu için yürütülen komutun sonucunu sunucuya iletir.
    /// </summary>
    public Task SubmitCommandResult(Guid requestId, int exitCode, string stdOut, string stdErr, long durationMs, bool timedOut, bool elevationDenied)
    {
        _commandManager.CompleteCommand(new DeviceCommandExecutionResult(
            requestId, exitCode, stdOut ?? string.Empty, stdErr ?? string.Empty, durationMs, timedOut, elevationDenied));
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _access.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
