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

    public SignalingHub(RemoteSessionRegistry sessions, DeviceRegistry devices)
    {
        _sessions = sessions;
        _devices = devices;
    }

    /// <summary>
    /// Teknisyen masaüstü uygulamasının geçerli bir oturum token'ı ile canlı oturum odasına katılması.
    /// Başarılı katılımda hedef cihaza "RemoteSessionRequested" sinyali gönderilir.
    /// </summary>
    /// <param name="sessionId">Teknisyen oturum kimliği.</param>
    /// <param name="token">Oturuma özel tek kullanımlık güvenlik token'ı.</param>
    public async Task JoinTechnicianSession(Guid sessionId, string token)
    {
        var session = _sessions.Get(sessionId);
        if (session is null || session.Token != token)
        {
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
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
        await Clients.Group($"session:{sessionId}").SendAsync("DeviceJoinedSession");
    }

    /// <summary>
    /// Oturum odasındaki diğer tarafa (Teknisyen -> Agent veya Agent -> Teknisyen) sinyal mesajı iletme.
    /// Mesaj türleri: screen-info, screen-frame-multi, remote-input, remote-command, command-result, file-chunk vb.
    /// </summary>
    /// <param name="sessionId">Oturum kimliği.</param>
    /// <param name="type">Sinyal türü.</param>
    /// <param name="payload">Sinyalin JSON veri gövdesi.</param>
    public async Task SendSignal(Guid sessionId, string type, string payload)
    {
        if (_sessions.Get(sessionId) is null)
        {
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        if (string.IsNullOrWhiteSpace(type) || payload is null)
        {
            throw new HubException("Sinyal veri gövdesi gereklidir.");
        }

        // Fare/klavye girdilerinde boyut güvenlik sınırı
        if (string.Equals(type, "remote-input", StringComparison.OrdinalIgnoreCase) && payload.Length > 4096)
        {
            throw new HubException("Uzak girdi paketi izin verilen boyutu aşıyor.");
        }

        // Mesajı oturumdaki diğer istemcilere yayınla
        await Clients.OthersInGroup($"session:{sessionId}").SendAsync("SignalReceived", type, payload);
    }
}
