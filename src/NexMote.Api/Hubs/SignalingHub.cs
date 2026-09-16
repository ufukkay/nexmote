using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using NexMote.Api.Services;
using NexMote.Shared.Contracts;

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
    private readonly DeviceCommandQueue _commandQueue;
    private readonly SecurityProfileService _securityProfiles;
    private readonly ILogger<SignalingHub> _logger;

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
        DeviceCommandManager commandManager,
        DeviceCommandQueue commandQueue,
        SecurityProfileService securityProfiles,
        ILogger<SignalingHub> logger)
    {
        _sessions = sessions;
        _devices = devices;
        _access = access;
        _commandManager = commandManager;
        _commandQueue = commandQueue;
        _securityProfiles = securityProfiles;
        _logger = logger;
    }

    /// <summary>
    /// Teknisyen masaüstü uygulamasının geçerli bir oturum token'ı ile canlı oturum odasına katılması.
    /// Başarılı katılımda güvenlik profili denetlenir; onay gerekmiyorsa "RemoteSessionRequested", gerekiyorsa "PromptConsentRequested" gönderilir.
    /// </summary>
    /// <param name="sessionId">Teknisyen oturum kimliği.</param>
    /// <param name="token">Oturuma özel tek kullanımlık güvenlik token'ı.</param>
    public async Task<string> JoinTechnicianSession(Guid sessionId, string token)
    {
        Guid? ownerUserId = null;
        var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(userIdClaim, out var parsedUserId))
        {
            ownerUserId = parsedUserId;
        }

        var session = _sessions.Activate(sessionId, token, ownerUserId);
        if (session is null)
        {
            _logger.LogWarning("[Signaling] JoinTechnicianSession basarisiz: Gecersiz veya suresi dolmus oturum. SessionId: {SessionId}", sessionId);
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _access.Add(Context.ConnectionId, sessionId, SignalSessionRole.Technician);
        _logger.LogInformation("[Signaling] Teknisyen katildi: SessionId={SessionId}, Hedef Cihaz={DeviceId}, ConnectionId={ConnectionId}", sessionId, session.DeviceId, Context.ConnectionId);

        // Cihazın etkin güvenlik profilini kontrol et
        var profile = _securityProfiles.GetEffectiveProfile(session.DeviceId);
        var consentRequired = false;

        if (profile is not null)
        {
            if (string.Equals(profile.ConsentMode, SecurityProfileConstants.ConsentAlwaysPrompt, StringComparison.OrdinalIgnoreCase))
            {
                consentRequired = true;
            }
            else if (string.Equals(profile.ConsentMode, SecurityProfileConstants.ConsentPromptIfActive, StringComparison.OrdinalIgnoreCase))
            {
                var device = _devices.Get(session.DeviceId);
                if (!string.IsNullOrWhiteSpace(device?.ActiveUser) && !device.ActiveUser.EndsWith("$", StringComparison.OrdinalIgnoreCase))
                {
                    consentRequired = true;
                }
            }
        }

        if (consentRequired && profile is not null)
        {
            _logger.LogInformation("[Signaling] Baglanti onayi gerekiyor: SessionId={SessionId}, TargetDeviceId={DeviceId}", sessionId, session.DeviceId);
            // Teknisyene onay beklendiğini bildir
            await Clients.Group($"session:{sessionId}").SendAsync("SessionStatusChanged", "waiting_consent");
            // Hedef cihaza onay diyaloğunu açma sinyali ilet
            var consentRequest = new ConnectionConsentRequest(
                sessionId,
                "NexMote Teknisyeni",
                profile.ConsentTimeoutSeconds,
                profile.ConsentDefaultAction);
            await Clients.Group($"device:{session.DeviceId}").SendAsync("PromptConsentRequested", consentRequest);
        }
        else
        {
            // Doğrudan bağlan (Unattended) - Çift katmanlı iletim: Hem Tray hem Windows Service'e gönder
            _logger.LogInformation("[Signaling] Canli oturum istegi iletiliyor: SessionId={SessionId}, TargetDeviceId={DeviceId}", sessionId, session.DeviceId);
            await Clients.Group($"device:{session.DeviceId}").SendAsync("RemoteSessionRequested", sessionId);
            await Clients.Group($"device:{session.DeviceId}:service").SendAsync("RemoteSessionRequested", sessionId);
        }

        return session.Token;
    }

    /// <summary>
    /// Hedef bilgisayardaki kullanıcının bağlantı onayı sonucunu iletmesi (Kabul veya Red).
    /// </summary>
    public async Task SubmitConsentResponse(Guid sessionId, Guid deviceId, string agentToken, bool accepted, string? reason)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            _logger.LogWarning("[Signaling] SubmitConsentResponse basarisiz: Gecersiz token. DeviceId={DeviceId}", deviceId);
            throw new HubException("Geçersiz cihaz token'ı.");
        }

        var session = _sessions.Get(sessionId);
        if (session is null || session.DeviceId != deviceId)
        {
            return;
        }

        if (accepted)
        {
            _logger.LogInformation("[Signaling] Kullanici onayi kabul edildi: SessionId={SessionId}, DeviceId={DeviceId}", sessionId, deviceId);
            await Clients.Group($"session:{sessionId}").SendAsync("SessionStatusChanged", "consent_accepted");
            await Clients.Group($"device:{deviceId}").SendAsync("RemoteSessionRequested", sessionId);
            await Clients.Group($"device:{deviceId}:service").SendAsync("RemoteSessionRequested", sessionId);
        }
        else
        {
            _logger.LogInformation("[Signaling] Kullanici onayi reddedildi: SessionId={SessionId}, Reason={Reason}", sessionId, reason);
            await Clients.Group($"session:{sessionId}").SendAsync("ConsentRejected", reason ?? "Hedef kullanıcı bağlantı isteğini reddetti.");
            await Clients.Group($"session:{sessionId}").SendAsync("SessionStatusChanged", "consent_rejected");
            _sessions.Expire(sessionId);
        }
    }

    /// <summary>
    /// Hedef makinedeki Agent'ın sunucuya sürekli açık tuttuğu arka plan dinleme kanalına bağlanması.
    /// Uzaktan oturum açma veya güncelleme bildirimleri bu gruba iletilir.
    /// </summary>
    /// <param name="deviceId">Cihazın benzersiz kimliği.</param>
    /// <param name="agentToken">Cihazın kimlik doğrulama token'ı.</param>
    /// <param name="clientType">İstemci türü ("service" veya "tray").</param>
    public async Task JoinDevice(Guid deviceId, string agentToken, string? clientType = null)
    {
        if (!_devices.ValidateAgent(deviceId, agentToken))
        {
            _logger.LogWarning("[Signaling] JoinDevice reddedildi: Gecersiz token. DeviceId={DeviceId}, ConnectionId={ConnectionId}", deviceId, Context.ConnectionId);
            throw new HubException("Geçersiz cihaz token'ı.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}");
        if (string.Equals(clientType, "service", StringComparison.OrdinalIgnoreCase))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}:service");
            _access.AddServiceConnection(Context.ConnectionId, deviceId);
        }
        else
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, $"device:{deviceId}:tray");
        }
        _access.AddDeviceConnection(Context.ConnectionId, deviceId);
        _logger.LogInformation("[Signaling] Cihaz dinleme kanalina baglandi: DeviceId={DeviceId}, ClientType={ClientType}, ConnectionId={ConnectionId}", deviceId, clientType ?? "tray", Context.ConnectionId);
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
            _logger.LogWarning("[Signaling] JoinDeviceSession reddedildi: SessionId={SessionId}, DeviceId={DeviceId}, HasSession={HasSession}", sessionId, deviceId, session is not null);
            throw new HubException("Geçersiz veya süresi dolmuş cihaz oturumu.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
        _access.Add(Context.ConnectionId, sessionId, SignalSessionRole.Agent);
        _access.AddDeviceConnection(Context.ConnectionId, deviceId);
        _logger.LogInformation("[Signaling] Cihaz oturum odasina katildi: DeviceId={DeviceId} -> SessionId={SessionId}, ConnectionId={ConnectionId}. Teknisyene bildirim iletiliyor.", deviceId, sessionId, Context.ConnectionId);
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
        _logger.LogInformation("[Signaling] SendSignal: SessionId={SessionId}, Type={Type}, Length={Length}, ConnectionId={ConnectionId}", sessionId, type, payload?.Length ?? 0, Context.ConnectionId);

        var session = _sessions.Get(sessionId);
        if (session is null)
        {
            _logger.LogWarning("[Signaling] SendSignal reddedildi: Oturum bulunamadi veya suresi dolmus. SessionId={SessionId}, Type={Type}, ConnectionId={ConnectionId}", sessionId, type, Context.ConnectionId);
            throw new HubException("Geçersiz veya süresi dolmuş oturum.");
        }

        if (!_access.Has(Context.ConnectionId, sessionId))
        {
            // Eğer bu bağlantı JoinDevice ile bu oturumun hedef cihazına ait olarak kaydedilmişse,
            // yeniden bağlanma yarış durumunu (race condition) önlemek için otomatik olarak oturum odasına dahil et
            if (_access.IsDeviceConnection(Context.ConnectionId, session.DeviceId))
            {
                _access.Add(Context.ConnectionId, sessionId, SignalSessionRole.Agent);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"session:{sessionId}");
                _logger.LogInformation("[Signaling] SendSignal: Ajan bağlantısı otomatik olarak oturum odasına dahil edildi. SessionId={SessionId}, DeviceId={DeviceId}, ConnectionId={ConnectionId}, Type={Type}", sessionId, session.DeviceId, Context.ConnectionId, type);
            }
            else
            {
                _logger.LogWarning("[Signaling] SendSignal reddedildi: Yetkisiz bağlantı. SessionId={SessionId}, Type={Type}, ConnectionId={ConnectionId}", sessionId, type, Context.ConnectionId);
                throw new HubException("Geçersiz veya süresi dolmuş oturum.");
            }
        }

        if (string.IsNullOrWhiteSpace(type) || payload is null)
        {
            throw new HubException("Sinyal türü ve veri gövdesi gereklidir.");
        }

        // Sunucu tarafı güvenlik profili kısıtlamaları (Madde 15)
        var role = _access.GetRole(Context.ConnectionId, sessionId);
        if (role == SignalSessionRole.Technician)
        {
            var profile = _securityProfiles.GetEffectiveProfile(session.DeviceId);
            if (profile != null)
            {
                if (profile.ViewOnlyMode && string.Equals(type, "remote-input", StringComparison.OrdinalIgnoreCase))
                {
                    // View-Only modunda klavye/fare girdisi hedefe iletilmez
                    return;
                }

                if (!profile.AllowRemoteTerminal && string.Equals(type, "remote-command", StringComparison.OrdinalIgnoreCase))
                {
                    throw new HubException("Bu cihaz için uzak terminal komut yürütme yetkisi kapalıdır.");
                }

                if (!profile.AllowClipboard && string.Equals(type, "clipboard-text", StringComparison.OrdinalIgnoreCase))
                {
                    throw new HubException("Bu cihaz için pano senkronizasyonu kapalıdır.");
                }

                if (!profile.AllowFileTransfer && string.Equals(type, "file-chunk", StringComparison.OrdinalIgnoreCase))
                {
                    throw new HubException("Bu cihaz için dosya aktarımı kapalıdır.");
                }
            }
        }

        // Mesaj tipine göre diferansiyel payload boyut sınırı uygula
        var limit = PayloadLimits.TryGetValue(type, out var configured) ? configured : DefaultPayloadLimit;
        if (payload.Length > limit)
        {
            _logger.LogWarning("[Signaling] Sinyal boyutu limiti asti: Type={Type}, Boyut={Length}, Limit={Limit}, SessionId={SessionId}", type, payload.Length, limit, sessionId);
            throw new HubException(
                $"'{type}' sinyali izin verilen maksimum boyutu ({limit:N0} karakter) aşıyor. " +
                $"Gelen: {payload.Length:N0} karakter.");
        }

        // Mesajı oturumdaki diğer istemcilere yayınla
        await Clients.OthersInGroup($"session:{sessionId}").SendAsync("SignalReceived", type, payload);

        // Kilit açma (send-sas / Ctrl+Alt+Del) sinyali geldiğinde en üst düzey SYSTEM yetkili Windows Servisine de doğrudan ilet
        if (string.Equals(type, "send-sas", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[Signaling] SAS (Ctrl+Alt+Del / Kilit Aç) sinyali SYSTEM servisine yönlendiriliyor: DeviceId={DeviceId}", session.DeviceId);
            await Clients.Group($"device:{session.DeviceId}:service").SendAsync("ExecuteSystemSas");
        }
    }

    /// <summary>
    /// Ajan tarafından doğrudan web konsolu için yürütülen komutun sonucunu sunucuya iletir.
    /// </summary>
    public Task SubmitCommandResult(Guid deviceId, Guid requestId, int exitCode, string stdOut, string stdErr, long durationMs, bool timedOut, bool elevationDenied)
    {
        if (!_access.IsDeviceConnection(Context.ConnectionId, deviceId))
        {
            throw new HubException("Geçersiz cihaz bağlantısı.");
        }

        var result = new DeviceCommandExecutionResult(
            requestId, exitCode, stdOut ?? string.Empty, stdErr ?? string.Empty, durationMs, timedOut, elevationDenied);
        if (!_commandQueue.Complete(deviceId, result))
        {
            throw new HubException("Command result is unknown or conflicts with the recorded result.");
        }
        _commandManager.CompleteCommand(deviceId, result);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Teknisyen istemcisinin veya web konsolunun canlı cihaz telemetri delta akışına abone olmasını sağlar (Madde 7).
    /// </summary>
    [Authorize(Policy = "AnyUser")]
    public Task SubscribeToDeviceFeed()
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, "devices:feed");
    }

    /// <summary>
    /// Canlı cihaz telemetri delta akışı aboneliğinden ayrılır.
    /// </summary>
    [Authorize(Policy = "AnyUser")]
    public Task UnsubscribeFromDeviceFeed()
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, "devices:feed");
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("[Signaling] Istemci baglantisi kesildi: ConnectionId={ConnectionId}, Exception={Exception}", Context.ConnectionId, exception?.Message ?? "Normal kapanis");
        _access.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
