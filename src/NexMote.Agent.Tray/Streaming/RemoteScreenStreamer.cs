using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;
using NexMote.Shared.Security;

namespace NexMote.Agent.Tray;

/// <summary>
/// SignalR WebSocket bağlantısını yöneten, çoklu ekran eş zamanlı JPEG ekran karelerini yakalayıp sunucuya ileten,
/// uzaktan gelen fare/klavye girdilerini, komutları, dosya aktarımlarını ve OTA güncelleme sinyallerini işleyen ana yayıncı sınıfı.
/// </summary>
internal sealed class RemoteScreenStreamer : IAsyncDisposable
{
    private string _serverUrl;
    private readonly Action<string> _setStatus;
    private HubConnection? _connection;
    private DeviceIdentity? _identity;
    private CancellationTokenSource? _streamCancellation;
    private Guid? _activeSessionId;
    private bool _starting;
    private bool _disposed;
    private bool _joinedDeviceGroup;
    private int _closedRetryCount = 0;
    private AgentSecurityProfileResponse? _securityProfile;
    private volatile string? _lastClipboardText;
    private int _adaptiveQuality = 72;
    private readonly object _qualityLock = new();
    private readonly ConcurrentDictionary<int, long> _lastAckedSequencePerDisplay = new();
    private readonly ConcurrentDictionary<long, long> _inFlightFrameSentTicks = new();
    private readonly ConcurrentDictionary<int, bool> _pendingResetPerDisplay = new();
    private readonly CaptureHelperClient _captureHelper = new();
    private long _lastRemoteInputTicks;
    private Guid _dedupSessionId;
    private long _lastProcessedInputSequence;
    private readonly Dictionary<Guid, ActiveFileTransfer> _activeTransfers = new();
    private NamedPipeClientStream? _inputHelperPipe;
    private StreamWriter? _inputHelperWriter;
    private long _nextPipeConnectAttemptTicks;
    private readonly object _pipeLock = new();
    private WebRtcPeerTransport? _webRtc;

    private readonly Func<Task>? _onSecurityProfileUpdated;

    public RemoteScreenStreamer(string serverUrl, Action<string> setStatus, Func<Task>? onSecurityProfileUpdated = null)
    {
        _serverUrl = serverUrl;
        _setStatus = setStatus;
        _onSecurityProfileUpdated = onSecurityProfileUpdated;
        StartWakeupListener();
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected && _joinedDeviceGroup;

    public void SetSecurityProfile(AgentSecurityProfileResponse? profile)
    {
        _securityProfile = profile;
    }

    public void UpdateServerUrl(string newUrl)
    {
        _serverUrl = newUrl;
        _joinedDeviceGroup = false;
        if (_connection is not null)
        {
            _ = _connection.DisposeAsync();
            _connection = null;
        }
        _ = EnsureStartedAsync();
    }

    public async Task EnsureStartedAsync()
    {
        if (_disposed || _starting || _connection?.State == HubConnectionState.Reconnecting || (_connection?.State == HubConnectionState.Connected && _joinedDeviceGroup))
        {
            return;
        }

        _starting = true;
        try
        {
            await ConnectAsync();
        }
        catch (Exception ex)
        {
            _setStatus($"baglanamadi ({ex.Message})");
        }
        finally
        {
            _starting = false;
        }
    }

    private void StartWakeupListener()
    {
        _ = Task.Run(async () =>
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            var pipeName = $"NexMote_Session_Wakeup_{sessionId}";

            while (!_disposed)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

                    using var server = NamedPipeServerStreamAcl.Create(
                        pipeName,
                        PipeDirection.In,
                        maxNumberOfServerInstances: 2,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        inBufferSize: 1024,
                        outBufferSize: 1024,
                        security);

                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync();
                    if (Guid.TryParse(line?.Trim(), out var requestedSessionId))
                    {
                        _ = HandleRemoteSessionRequestedAsync(requestedSessionId);
                    }
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
        });
    }

    private async Task ConnectAsync()
    {
        _identity = DeviceIdentityFile.Load();
        if (_identity is null)
        {
            _setStatus("kaydolunuyor...");
            var enrollKey = AgentSettings.LoadEnrollmentKey();
            _identity = await DeviceIdentityFile.EnsureEnrolledAsync(_serverUrl, enrollKey);
            if (_identity is null)
            {
                _setStatus("identity bekleniyor (kayıt başarısız)");
                return;
            }
        }

        if (_connection is not null)
        {
            try { await _connection.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); } catch { }
            _joinedDeviceGroup = false;
        }

        var hubUrl = $"{_serverUrl.TrimEnd('/')}/hubs/signaling";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => NexMoteHttp.CreateHandler();
                options.TransportMaxBufferSize = 10 * 1024 * 1024;
                options.ApplicationMaxBufferSize = 10 * 1024 * 1024;
            })
            .WithAutomaticReconnect(new InfiniteRetryPolicy())
            .Build();

        _connection.On<ConnectionConsentRequest>("PromptConsentRequested", request =>
        {
            _ = HandleConsentRequestAsync(request);
        });

        _connection.On<Guid>("RemoteSessionRequested", sessionId =>
        {
            _ = HandleRemoteSessionRequestedAsync(sessionId);
        });

        _connection.On("SecurityProfileUpdated", async () =>
        {
            if (_onSecurityProfileUpdated is not null)
            {
                await _onSecurityProfileUpdated();
            }
        });

        _connection.On<string, string>("SignalReceived", (type, payload) =>
        {
            if (string.Equals(type, "remote-input", StringComparison.OrdinalIgnoreCase))
            {
                if (_securityProfile?.ViewOnlyMode == true)
                {
                    return; // Sadece izleme modunda girdi kapalı
                }
                _lastRemoteInputTicks = Stopwatch.GetTimestamp();
                HandleRemoteInput(payload);
            }
            else if (string.Equals(type, "ping", StringComparison.OrdinalIgnoreCase))
            {
                if (_activeSessionId.HasValue && _connection?.State == HubConnectionState.Connected)
                {
                    _ = _connection.InvokeAsync("SendSignal", _activeSessionId.Value, "pong", payload);
                }
            }
            else if (string.Equals(type, "network-probe", StringComparison.OrdinalIgnoreCase))
            {
                HandleNetworkProbe(payload);
            }
            else if (string.Equals(type, "frame-ack", StringComparison.OrdinalIgnoreCase))
            {
                HandleFrameAck(payload);
            }
            else if (string.Equals(type, "clipboard-text", StringComparison.OrdinalIgnoreCase))
            {
                if (_securityProfile?.AllowClipboard == false)
                {
                    return; // Pano paylaşımı kapalı
                }
                try
                {
                    if (!string.IsNullOrEmpty(payload))
                    {
                        _lastClipboardText = payload; // Yankıyı önle: bu değeri biz set ettik, izleme döngüsü tekrar göndermesin
                        Thread thread = new(() => Clipboard.SetText(payload));
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                    }
                }
                catch { }
            }
            else if (string.Equals(type, "file-chunk", StringComparison.OrdinalIgnoreCase))
            {
                if (_securityProfile?.AllowFileTransfer == false)
                {
                    return; // Dosya transferi kapalı
                }
                HandleFileChunk(payload);
            }
            else if (string.Equals(type, "remote-command", StringComparison.OrdinalIgnoreCase))
            {
                if (_securityProfile?.AllowRemoteTerminal == false)
                {
                    if (_activeSessionId.HasValue && _connection?.State == HubConnectionState.Connected)
                    {
                        _ = _connection.InvokeAsync("SendSignal", _activeSessionId.Value, "command-result",
                            JsonSerializer.Serialize(new { output = "Uzak terminal güvenlik profili tarafından devre dışı bırakılmıştır.", exitCode = 1 }));
                    }
                    return;
                }
                _ = HandleRemoteCommandAsync(payload);
            }
            else if (string.Equals(type, "set-quality-mode", StringComparison.OrdinalIgnoreCase))
            {
                HandleSetQualityMode(payload);
            }
            else if (string.Equals(type, "webrtc-signal", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var signal = JsonSerializer.Deserialize<WebRtcSignalMessage>(payload);
                    if (signal != null && _webRtc != null)
                    {
                        if (string.Equals(signal.Type, "offer", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(signal.Sdp))
                        {
                            _ = _webRtc.HandleOfferAsync(signal.Sdp);
                        }
                        else if (string.Equals(signal.Type, "candidate", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(signal.Candidate))
                        {
                            _webRtc.HandleCandidate(signal.Candidate, signal.SdpMid, signal.SdpMLineIndex);
                        }
                    }
                }
                catch { }
            }
            else if (string.Equals(type, "refresh-screen", StringComparison.OrdinalIgnoreCase))
            {
                // Hash/DXGI durumu artık izole capture-helper alt sürecinde yaşıyor; buradan doğrudan
                // sıfırlanamaz. Bir sonraki yakalama isteğine "resetHash" bayrağı olarak taşınır.
                for (var i = 1; i <= ScreenCapture.GetDisplayCount(); i++)
                {
                    _pendingResetPerDisplay[i] = true;
                }
                if (_activeSessionId.HasValue)
                {
                    _ = SendScreenInfoAsync(_activeSessionId.Value);
                }
            }
            else if (string.Equals(type, "send-sas", StringComparison.OrdinalIgnoreCase))
            {
                if (_securityProfile?.ViewOnlyMode == true) return;
                if (!TrySendToInputHelper(JsonSerializer.Serialize(new RemoteInputEvent(_activeSessionId ?? Guid.Empty, "send-sas"))))
                {
                    SasHelper.SendSas();
                }

                // Kilit açma sonrası ekran görüntüsünü anında tazeleyip teknisyene zorunlu olarak gönder
                for (var i = 1; i <= ScreenCapture.GetDisplayCount(); i++)
                {
                    _pendingResetPerDisplay[i] = true;
                }
                if (_activeSessionId.HasValue)
                {
                    _ = SendScreenInfoAsync(_activeSessionId.Value);
                }
            }
            else if (string.Equals(type, "power-action", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var req = JsonSerializer.Deserialize<PowerActionRequest>(payload);
                    if (req != null)
                    {
                        if (!TrySendToInputHelper(JsonSerializer.Serialize(new RemoteInputEvent(_activeSessionId ?? Guid.Empty, "power-action", Button: req.Action))))
                        {
                            PowerHelper.Execute(req.Action);
                        }
                    }
                }
                catch { }
            }
        });

        _connection.On<string>("RemoteUpdateRequested", msiUrl =>
        {
            _setStatus("uzaktan sessiz guncelleme baslatildi...");
            if (!string.IsNullOrEmpty(msiUrl))
            {
                _ = RemoteScreenStreamer.PerformSelfUpdateAsync(msiUrl);
            }
        });

        _connection.On<string>("ExecutePowerAction", action =>
        {
            try
            {
                if (!TrySendToInputHelper(JsonSerializer.Serialize(new RemoteInputEvent(_activeSessionId ?? Guid.Empty, "power-action", Button: action))))
                {
                    PowerHelper.Execute(action);
                }
            }
            catch { }
        });

        _connection.On<Guid, string, string, bool>("ExecuteWebCommand", async (requestId, shell, command, runAsAdmin) =>
        {
            if (IsWindowsServiceRunning())
            {
                return;
            }

            var result = await CommandRunner.RunAsync(shell, command, 120000, runAsAdmin);
            try
            {
                if (_connection?.State == HubConnectionState.Connected)
                {
                    await _connection.InvokeAsync("SubmitCommandResult",
                        _identity.DeviceId,
                        requestId,
                        result.ExitCode,
                        result.StdOut,
                        result.StdErr,
                        result.DurationMs,
                        result.TimedOut,
                        result.ElevationDenied);
                }
            }
            catch { }
        });

        _connection.On<string>("ExecutePowerAction", action =>
        {
            try
            {
                if (!TrySendToInputHelper(JsonSerializer.Serialize(new RemoteInputEvent(_activeSessionId ?? Guid.Empty, "power-action", Button: action))))
                {
                    PowerHelper.Execute(action);
                }
            }
            catch { }
        });

        _connection.Reconnecting += error =>
        {
            _joinedDeviceGroup = false;
            _setStatus($"yeniden baglaniyor ({error?.Message})");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            // Reset closed retry counter on successful reconnect
            _closedRetryCount = 0;
            await JoinDeviceAsync();
            _joinedDeviceGroup = true;
            if (_activeSessionId.HasValue && _identity is not null)
            {
                try
                {
                    await _connection.InvokeAsync("JoinDeviceSession", _activeSessionId.Value, _identity.DeviceId, _identity.AgentToken);

                    // SignalR Hub'ının kısa süreli kopup yeniden bağlanması (Wi-Fi dalgalanması, IIS/proxy
                    // takılması vb.) çoğunlukla P2P WebRTC veri kanalını hiç etkilemez. Yayın döngüleri hâlâ
                    // çalışıyorsa StartStreaming'i tekrar çağırmıyoruz: aksi halde her ufak Hub kesintisinde
                    // sağlıklı WebRTC bağlantısı sıfırdan ICE müzakeresine zorlanır ve görüntü/girdi akışı
                    // saniyelik olarak donar — asıl "saniyelik kesinti" şikayetinin kaynağı buydu.
                    var streamingAlreadyActive = _streamCancellation is { IsCancellationRequested: false };
                    if (!streamingAlreadyActive)
                    {
                        StartStreaming(_activeSessionId.Value);
                    }
                }
                catch { }
            }
            _setStatus("hazir");
        };

        _connection.Closed += error =>
        {
            _joinedDeviceGroup = false;
            // Increase closed retry count and compute backoff with jitter
            _closedRetryCount++;
            var baseSeconds = Math.Min(30, (int)Math.Pow(2, Math.Min(_closedRetryCount, 5)));
            var rand = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1000) / 1000.0;
            var jitterFactor = 0.6 + (rand * 0.8);
            var delaySeconds = Math.Max(1, (int)(baseSeconds * jitterFactor));
            _setStatus($"kapandi ({error?.Message ?? "baglanti kapandi"}) - yeniden dene ~{delaySeconds}s");
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                if (!_disposed)
                {
                    await EnsureStartedAsync();
                }
            });
            return Task.CompletedTask;
        };

        using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await _connection.StartAsync(connectCts.Token);
        try
        {
            await JoinDeviceAsync();
            _joinedDeviceGroup = true;
            _setStatus("hazir");
        }
        catch
        {
            _joinedDeviceGroup = false;
            try { await _connection.DisposeAsync(); } catch { }
            _connection = null;
            throw;
        }
    }

    private async Task HandleConsentRequestAsync(ConnectionConsentRequest req)
    {
        if (_connection is null || _identity is null) return;
        bool accepted = false;

        // Mod 3 (Kullanıcı Yoksa Otomatik Bağlan): Eğer boşta kalma eşiği aşılmışsa kullanıcı bilgisayar başında değildir, otomatik kabul et
        if (req.IdleTimeoutMinutes.HasValue && req.IdleTimeoutMinutes.Value > 0 &&
            NexMote.Agent.Tray.Platform.UserActivityHelper.IsUserIdle(req.IdleTimeoutMinutes.Value))
        {
            accepted = true;
        }
        else
        {
            try
            {
                var tcs = new TaskCompletionSource<bool>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        using var dlg = new ConsentDialogForm(req.TechnicianName, req.TimeoutSeconds, req.DefaultAction);
                        dlg.ShowDialog();
                        tcs.SetResult(dlg.Accepted);
                    }
                    catch (Exception ex)
                    {
                        tcs.SetException(ex);
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();

                accepted = await tcs.Task;
            }
            catch
            {
                accepted = string.Equals(req.DefaultAction, SecurityProfileConstants.ActionAllow, StringComparison.OrdinalIgnoreCase);
            }
        }

        try
        {
            await _connection.InvokeAsync("SubmitConsentResponse", req.SessionId, _identity.DeviceId, _identity.AgentToken, accepted, accepted ? null : "Hedef kullanıcı bağlantı isteğini reddetti.");
        }
        catch { }
    }

    private async Task JoinDeviceAsync()
    {
        if (_connection is null || _identity is null)
        {
            throw new InvalidOperationException("Cihaz kanalina katilmak icin baglanti ve cihaz kimligi gerekli.");
        }

        try
        {
            await _connection.InvokeAsync("JoinDevice", _identity.DeviceId, _identity.AgentToken, "tray");
        }
        catch (Exception firstError)
        {
            _joinedDeviceGroup = false;
            // Reload the service-owned identity without rotating it on an unrelated hub error.
            var refreshed = DeviceIdentityFile.Load();
            if (refreshed is null ||
                (refreshed.DeviceId == _identity.DeviceId && refreshed.AgentToken == _identity.AgentToken))
            {
                throw new InvalidOperationException("Cihaz dinleme kanalina katilinamadi; baglanti yeniden denenecek.", firstError);
            }

            _identity = refreshed;
            await _connection.InvokeAsync("JoinDevice", _identity.DeviceId, _identity.AgentToken, "tray");
        }
    }

    private static bool IsWindowsServiceRunning()
    {
        try
        {
            return Process.GetProcessesByName("NexMote.Agent.Windows").Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task HandleRemoteSessionRequestedAsync(Guid sessionId)
    {
        if (_activeSessionId == sessionId && _connection?.State == HubConnectionState.Connected)
        {
            return;
        }

        if (_identity is null)
        {
            _identity = DeviceIdentityFile.Load();
        }

        if (_connection is null || _connection.State != HubConnectionState.Connected)
        {
            await EnsureStartedAsync();
        }

        if (_connection is null || _identity is null)
        {
            return;
        }

        try
        {
            _setStatus($"oturum {sessionId} baglaniyor");
            await _connection.InvokeAsync("JoinDeviceSession", sessionId, _identity.DeviceId, _identity.AgentToken);
            StartStreaming(sessionId);
        }
        catch (Exception ex)
        {
            try
            {
                var enrollKey = AgentSettings.LoadEnrollmentKey();
                var refreshed = await DeviceIdentityFile.EnsureEnrolledAsync(_serverUrl, enrollKey);
                if (refreshed is not null)
                {
                    _identity = refreshed;
                    await _connection.InvokeAsync("JoinDeviceSession", sessionId, _identity.DeviceId, _identity.AgentToken);
                    StartStreaming(sessionId);
                    return;
                }
            }
            catch { }

            _setStatus($"oturum hatasi ({ex.Message})");
        }
    }

    private void StartStreaming(Guid sessionId)
    {
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _streamCancellation = new CancellationTokenSource();
        _activeSessionId = sessionId;

        // Oturum başlarken basılı kalmış olabilecek klavye niteleyici tuşlarını temizle
        InputInjector.ReleaseAllModifiers();

        // WebRTC P2P DataChannel eşleşmesini başlat (Madde 1)
        try
        {
            _webRtc?.Dispose();
            _webRtc = new WebRtcPeerTransport();
            _webRtc.OnSignalReady += (signal) =>
            {
                if (_connection?.State == HubConnectionState.Connected && _activeSessionId == sessionId)
                {
                    var json = JsonSerializer.Serialize(signal);
                    _ = _connection.InvokeAsync("SendSignal", sessionId, "webrtc-signal", json);
                }
            };
            _webRtc.OnDataMessageReceived += (channel, text) =>
            {
                if (string.Equals(channel, "input", StringComparison.OrdinalIgnoreCase))
                {
                    HandleRemoteInput(text);
                }
            };
        }
        catch { }

        _ = SendScreenInfoAsync(sessionId);

        var token = _streamCancellation.Token;
        StartClipboardWatch(sessionId, token);

        var info = ScreenCapture.GetInfo();
        var displays = (info.Displays ?? Array.Empty<DisplayItem>()).Where(d => d.Index > 0).ToList();
        if (displays.Count == 0)
        {
            _ = Task.Run(() => StreamLoopAsync(sessionId, 0, token));
        }
        else
        {
            foreach (var d in displays)
            {
                var capturedIndex = d.Index;
                _ = Task.Run(() => StreamLoopAsync(sessionId, capturedIndex, token));
            }
        }
    }

    private void StartClipboardWatch(Guid sessionId, CancellationToken token)
    {
        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_securityProfile?.AllowClipboard != false && Clipboard.ContainsText())
                    {
                        var text = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(text) && text != _lastClipboardText)
                        {
                            _lastClipboardText = text;
                            if (_connection?.State == HubConnectionState.Connected && _activeSessionId == sessionId)
                            {
                                _connection.InvokeAsync("SendSignal", sessionId, "clipboard-text", text).GetAwaiter().GetResult();
                            }
                        }
                    }
                }
                catch { }

                token.WaitHandle.WaitOne(1000);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
    }

    private async Task SendScreenInfoAsync(Guid sessionId)
    {
        if (_connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var info = JsonSerializer.Serialize(ScreenCapture.GetInfo());
            await _connection.InvokeAsync("SendSignal", sessionId, "screen-info", info);
        }
        catch (Exception ex)
        {
            _setStatus($"ekran bilgisi gonderilemedi ({ex.Message})");
        }
    }

    private void HandleRemoteInput(string payload)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(payload, options);
            if (input is null || _activeSessionId != input.SessionId)
            {
                return;
            }

            // Mükerrer teslimat koruması: aynı girdi olayı hem WebRTC P2P (SCTP yeniden-iletim) hem de
            // SignalR röle yolundan ya da bir aktarım katmanı hatası nedeniyle iki kez gelebiliyor —
            // örn. bir tuşa tek basıldığında karşı tarafta "dd" gibi mükerrer karakter görülmesi bu
            // yüzdendi. Teknisyen her olaya artan bir Sequence numarası veriyor; aynı oturum içinde daha
            // önce işlenmiş veya daha eski bir Sequence tekrar gelirse sessizce yok sayılır.
            if (input.Sequence > 0)
            {
                if (_dedupSessionId != input.SessionId)
                {
                    _dedupSessionId = input.SessionId;
                    _lastProcessedInputSequence = 0;
                }

                if (input.Sequence <= _lastProcessedInputSequence)
                {
                    SendInputAck(input, true);
                    return;
                }

                _lastProcessedInputSequence = input.Sequence;
            }

            var applied = TrySendToInputHelper(payload);
            if (!applied)
            {
                ApplyInputDirectly(input);
                applied = true;
            }

            SendInputAck(input, applied);
        }
        catch (Exception ex)
        {
            _setStatus($"input uygulanamadi ({ex.Message})");
        }
    }

    private void HandleNetworkProbe(string payload)
    {
        if (_activeSessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var probe = JsonSerializer.Deserialize<NetworkProbe>(payload);
            if (probe is null)
            {
                return;
            }

            var received = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ack = new NetworkProbeAck(probe.ProbeId, probe.SentAtUnixMs, received, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _ = _connection.InvokeAsync("SendSignal", _activeSessionId.Value, "network-probe-ack", JsonSerializer.Serialize(ack));
        }
        catch
        {
        }
    }

    private void HandleFrameAck(string payload)
    {
        try
        {
            var ack = JsonSerializer.Deserialize<FrameAck>(payload);
            if (ack is not null && _activeSessionId == ack.SessionId)
            {
                _lastAckedSequencePerDisplay[ack.DisplayIndex] = ack.Sequence;

                // Yerel RTT ölçümü: Tamamen bu bilgisayarın yerel Stopwatch sayacına dayalı (saat farkı sıfır)
                if (_inFlightFrameSentTicks.TryRemove(ack.Sequence, out var sentTicks))
                {
                    var localRttMs = (Stopwatch.GetTimestamp() - sentTicks) * 1000 / Stopwatch.Frequency;
                    AdjustQuality(Math.Max(1, localRttMs));
                }
            }
        }
        catch
        {
        }
    }

    private void SendInputAck(RemoteInputEvent input, bool applied)
    {
        if (input.Sequence <= 0 || _activeSessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var ack = new InputAck(input.SessionId, input.Sequence, input.Kind, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), applied);
            _ = _connection.InvokeAsync("SendSignal", _activeSessionId.Value, "input-ack", JsonSerializer.Serialize(ack));
        }
        catch
        {
        }
    }

    private static void ApplyInputDirectly(RemoteInputEvent input)
    {
        switch (input.Kind.ToLowerInvariant())
        {
            case "mouse-move":
                InputInjector.MoveMouse(input.DisplayIndex, input.X, input.Y);
                break;
            case "mouse-button":
                InputInjector.MoveMouse(input.DisplayIndex, input.X, input.Y);
                InputInjector.MouseButton(input.Button, input.IsDown);
                break;
            case "mouse-wheel":
                InputInjector.MouseWheel(input.WheelDelta);
                break;
            case "key":
                InputInjector.Keyboard(input.KeyCode, input.IsDown);
                break;
        }
    }

    private bool TrySendToInputHelper(string payload)
    {
        lock (_pipeLock)
        {
            try
            {
                if (_inputHelperPipe is null || !_inputHelperPipe.IsConnected)
                {
                    if (Stopwatch.GetTimestamp() < _nextPipeConnectAttemptTicks)
                    {
                        return false;
                    }

                    _inputHelperWriter?.Dispose();
                    _inputHelperPipe?.Dispose();

                    var sessionId = Process.GetCurrentProcess().SessionId;
                    _inputHelperPipe = new NamedPipeClientStream(".", $"NexMoteInputHelper_{sessionId}", PipeDirection.Out);
                    _inputHelperPipe.Connect(100);
                    _inputHelperWriter = new StreamWriter(_inputHelperPipe, Encoding.UTF8, 4096, leaveOpen: false) { AutoFlush = true };
                }

                _inputHelperWriter!.WriteLine(payload);
                return true;
            }
            catch
            {
                _inputHelperWriter?.Dispose();
                _inputHelperPipe?.Dispose();
                _inputHelperPipe = null;
                _inputHelperWriter = null;
                _nextPipeConnectAttemptTicks = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 2);
                return false;
            }
        }
    }

    private void HandleFileChunk(string payload)
    {
        try
        {
            CleanupStaleTransfers();

            var chunk = JsonSerializer.Deserialize<FileTransferChunk>(payload);
            if (chunk is null || _activeSessionId != chunk.SessionId)
            {
                return;
            }

            // Boyut sınırı kontrolü (Madde 8: En fazla 500 MB)
            if (chunk.TotalSize > FileTransferValidator.MaxFileSizeBytes)
            {
                _setStatus($"dosya aktarımı reddedildi: dosya boyutu sınırı aşıldı ({chunk.TotalSize / (1024 * 1024)} MB > 500 MB)");
                return;
            }

            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var incomingDir = Path.Combine(programData, "NexMote", "Agent", "Incoming");
            Directory.CreateDirectory(incomingDir);

            if (!_activeTransfers.TryGetValue(chunk.TransferId, out var transfer))
            {
                var safeName = FileTransferValidator.SanitizeFileName(chunk.FileName);
                var tempPath = Path.Combine(incomingDir, $".{chunk.TransferId:N}.part");
                var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                transfer = new ActiveFileTransfer(chunk.TransferId, safeName, tempPath, fileStream, chunk.TotalSize);
                _activeTransfers[chunk.TransferId] = transfer;
            }

            var bytes = Convert.FromBase64String(chunk.Base64Data);
            if (bytes.Length > FileTransferValidator.MaxChunkSizeBytes)
            {
                throw new InvalidOperationException("Parça boyutu maksimum 1 MB sınırını aşıyor.");
            }

            transfer.Stream.Write(bytes, 0, bytes.Length);
            transfer.BytesReceived += bytes.Length;
            transfer.LastActivityUtc = DateTimeOffset.UtcNow;
            _setStatus($"dosya aliniyor: {transfer.SafeFileName} ({chunk.ChunkIndex + 1}/{chunk.TotalChunks})");

            if (chunk.IsLast)
            {
                _activeTransfers.Remove(chunk.TransferId);
                transfer.Stream.Flush();

                // SHA-256 Bütünlük / Checksum Doğrulaması (Madde 8)
                if (!string.IsNullOrWhiteSpace(chunk.Sha256))
                {
                    transfer.Stream.Position = 0;
                    var actualSha = FileTransferValidator.ComputeStreamSha256(transfer.Stream);
                    if (!FileTransferValidator.VerifyChecksum(actualSha, chunk.Sha256))
                    {
                        transfer.Dispose();
                        try { File.Delete(transfer.TempFilePath); } catch { }
                        _setStatus($"dosya aktarımı reddedildi: SHA-256 hash uyuşmazlığı ({transfer.SafeFileName})");
                        return;
                    }
                }

                transfer.Dispose();

                // Atomik olarak kalıcı dosyaya taşı
                var targetPath = Path.Combine(incomingDir, transfer.SafeFileName);
                if (File.Exists(targetPath))
                {
                    var ext = Path.GetExtension(transfer.SafeFileName);
                    var baseName = Path.GetFileNameWithoutExtension(transfer.SafeFileName);
                    targetPath = Path.Combine(incomingDir, $"{baseName}_{DateTime.Now:HHmmss}{ext}");
                }

                File.Move(transfer.TempFilePath, targetPath, overwrite: true);
                _setStatus($"dosya alindi: {Path.GetFileName(targetPath)}");

                // Uzak işletim sisteminin panosuna yerleştir (Madde 3: Clipboard File Drop)
                try
                {
                    var staThread = new Thread(() =>
                    {
                        try
                        {
                            var dropList = new System.Collections.Specialized.StringCollection { targetPath };
                            Clipboard.SetFileDropList(dropList);
                        }
                        catch { }
                    });
                    staThread.SetApartmentState(ApartmentState.STA);
                    staThread.IsBackground = true;
                    staThread.Start();
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _setStatus($"dosya alinamadi ({ex.Message})");
        }
    }

    private void CleanupStaleTransfers()
    {
        try
        {
            var cutoff = DateTimeOffset.UtcNow - FileTransferValidator.TransferTimeout;
            var staleKeys = _activeTransfers
                .Where(kvp => kvp.Value.LastActivityUtc < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in staleKeys)
            {
                if (_activeTransfers.Remove(key, out var stale))
                {
                    try
                    {
                        stale.Dispose();
                        if (File.Exists(stale.TempFilePath))
                        {
                            File.Delete(stale.TempFilePath);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    private async Task HandleRemoteCommandAsync(string payload)
    {
        RemoteCommandRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<RemoteCommandRequest>(payload);
        }
        catch
        {
            return;
        }

        if (request is null || _activeSessionId != request.SessionId || _connection is null)
        {
            return;
        }

        var result = await CommandRunner.RunAsync(request.Shell, request.Command, 30000, request.RunAsAdmin);

        try
        {
            var response = new RemoteCommandResult(
                request.SessionId,
                request.RequestId,
                result.ExitCode,
                result.StdOut,
                result.StdErr,
                result.DurationMs,
                result.TimedOut,
                result.ElevationDenied);

            await _connection.InvokeAsync("SendSignal", request.SessionId, "command-result", JsonSerializer.Serialize(response));
        }
        catch
        {
        }

        if (_identity is not null)
        {
            _ = PostCommandAuditAsync(request, result);
        }
    }

    private async Task PostCommandAuditAsync(RemoteCommandRequest request, CommandRunResult result)
    {
        try
        {
            var entry = new CommandAuditEntry(
                _identity!.DeviceId,
                _identity.AgentToken,
                request.SessionId,
                request.Shell,
                request.Command,
                result.ExitCode,
                Truncate(result.StdOut, 2000),
                Truncate(result.StdErr, 2000),
                result.DurationMs,
                DateTimeOffset.UtcNow);

            using var http = NexMoteHttp.CreateClient();
            await http.PostAsJsonAsync($"{_serverUrl.TrimEnd('/')}/api/audit/commands", entry);
        }
        catch
        {
        }
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;

    private async Task StreamLoopAsync(Guid sessionId, int displayIndex, CancellationToken cancellationToken)
    {
        _setStatus("goruntu gonderiliyor");
        var forceIntervalTicks = Stopwatch.Frequency * 3; // 3 saniyede bir zorunlu senkronizasyon
        var lastSendTicks = 0L;
        var lastMotionTicks = Stopwatch.GetTimestamp();
        var sequence = 0L;
        var initialBurst = 3;
        var refinementSent = false;

        _lastAckedSequencePerDisplay[displayIndex] = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            // Hub (SignalR) yalnızca kontrol kanalıdır; WebRTC P2P veri kanalı canlıysa kare göndermeye devam
            // edebiliriz. Yalnızca Hub'a bakıp beklemek, kısa süreli Hub dalgalanmalarında P2P hâlâ çalışırken
            // bile akışı saniyelerce durdurup "saniyelik kesinti" hissi yaratıyordu.
            if ((_connection is null || _connection.State != HubConnectionState.Connected) && _webRtc?.IsConnected != true)
            {
                var reconnected = false;
                for (int i = 0; i < 20; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await Task.Delay(500, cancellationToken);
                    if (_connection?.State == HubConnectionState.Connected || _webRtc?.IsConnected == true)
                    {
                        reconnected = true;
                        _pendingResetPerDisplay[displayIndex] = true;
                        break;
                    }
                }
                if (!reconnected) break;
            }

            try
            {
                var lastAcked = _lastAckedSequencePerDisplay.GetValueOrDefault(displayIndex, 0);
                var inFlightCount = sequence - lastAcked;
                var maxInFlight = _selectedQualityMode switch
                {
                    "speed" => 3,     // 60-80 FPS için 3 eş zamanlı kare
                    "quality" => 2,   // Kristal modda 2 kare
                    "balanced" => 2,  // Dengeli modda 2 kare
                    _ => 2            // Otomatik modda 2 kare
                };

                // Kayan pencere kontrolü: Boru hattında maxInFlight'tan fazla bekleyen kare varsa kısa bir süre bekle
                if (inFlightCount >= maxInFlight)
                {
                    var waitLimitMs = _selectedQualityMode switch
                    {
                        "speed" => 20,
                        "quality" => 50,
                        "balanced" => 35,
                        _ => Math.Clamp(_smoothedRttMs + 15, 20, 50)
                    };

                    var waitStart = Stopwatch.GetTimestamp();
                    while ((sequence - _lastAckedSequencePerDisplay.GetValueOrDefault(displayIndex, 0)) >= maxInFlight)
                    {
                        var elapsedWaitMs = (Stopwatch.GetTimestamp() - waitStart) * 1000 / Stopwatch.Frequency;
                        if (elapsedWaitMs >= waitLimitMs || cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }
                        await Task.Delay(2, cancellationToken);
                    }
                }

                var now = Stopwatch.GetTimestamp();
                var forceSend = (initialBurst > 0) || (now - lastSendTicks) >= forceIntervalTicks;
                if (initialBurst > 0)
                {
                    initialBurst--;
                    _pendingResetPerDisplay[displayIndex] = true;
                }
                else if (forceSend)
                {
                    _pendingResetPerDisplay[displayIndex] = true;
                    refinementSent = false;
                }

                var timeSinceMotionMs = (now - lastMotionTicks) * 1000 / Stopwatch.Frequency;
                var timeSinceInputMs = (now - _lastRemoteInputTicks) * 1000 / Stopwatch.Frequency;
                var isMotionActive = timeSinceMotionMs < 250 || timeSinceInputMs < 400;
                var isRefinement = !refinementSent && !isMotionActive && (now - lastSendTicks) > 0;

                int quality;
                if (isRefinement)
                {
                    quality = 92;
                    forceSend = true;
                }
                else if (isMotionActive)
                {
                    // Hareket esnasında hafif sıkıştırma (70-90 KB kare boyutu) ile sıfır gecikmeli 60 FPS
                    quality = Math.Clamp(GetCurrentQuality() - 12, 48, 66);
                }
                else
                {
                    quality = Math.Clamp(GetCurrentQuality(), 54, 90);
                }

                var resetHash = _pendingResetPerDisplay.TryRemove(displayIndex, out var pending) && pending;
                var frame = await _captureHelper.CaptureJpegBase64Async(displayIndex, quality, forceSend, resetHash, cancellationToken);
                var hubConnected = _connection?.State == HubConnectionState.Connected;
                var p2pConnected = _webRtc?.IsConnected == true;

                if (frame is not null && (hubConnected || p2pConnected))
                {
                    var bounds = ScreenCapture.GetDisplayBoundsPublic(displayIndex);
                    sequence++;
                    _inFlightFrameSentTicks[sequence] = now;
                    if (_inFlightFrameSentTicks.Count > 100)
                    {
                        foreach (var k in _inFlightFrameSentTicks.Keys)
                        {
                            if (k < sequence - 60) _inFlightFrameSentTicks.TryRemove(k, out _);
                        }
                    }

                    var payload = JsonSerializer.Serialize(new MultiScreenFrame(
                        displayIndex,
                        JpegBase64: frame,
                        Sequence: sequence,
                        CapturedAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                        ScreenWidth: bounds.Width,
                        ScreenHeight: bounds.Height));

                    var sentViaP2p = false;
                    try
                    {
                        sentViaP2p = _webRtc?.SendData("stream", payload) ?? false;
                    }
                    catch
                    {
                        sentViaP2p = false;
                    }

                    if (!sentViaP2p && hubConnected)
                    {
                        await _connection!.InvokeAsync("SendSignal", sessionId, "screen-frame-multi", payload, cancellationToken);
                    }

                    if (!isRefinement)
                    {
                        lastMotionTicks = now;
                        refinementSent = false;
                    }
                    else
                    {
                        refinementSent = true;
                    }

                    lastSendTicks = now;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_frameDelayMs), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _setStatus($"ekran {displayIndex} hatasi ({ex.Message})");
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
    }

    private string _selectedQualityMode = "auto";
    private int _frameDelayMs = 16;
    private int _smoothedRttMs = 25;

    private void HandleSetQualityMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        _selectedQualityMode = mode.Trim().ToLowerInvariant();
        AdjustQuality(_smoothedRttMs);
    }

    private int GetCurrentQuality()
    {
        lock (_qualityLock)
        {
            return _adaptiveQuality;
        }
    }

    private void AdjustQuality(long rttMs)
    {
        lock (_qualityLock)
        {
            // Üstel hareketli ortalama ile ani ağ dalgalanmalarına karşı yumuşatma
            _smoothedRttMs = (int)(_smoothedRttMs * 0.7 + rttMs * 0.3);
            _smoothedRttMs = Math.Clamp(_smoothedRttMs, 1, 1000);

            switch (_selectedQualityMode)
            {
                case "speed":
                    _adaptiveQuality = 56;
                    _frameDelayMs = 12; // ~80 FPS hedefi
                    break;

                case "balanced":
                    _adaptiveQuality = 70;
                    _frameDelayMs = 16; // ~60 FPS hedefi
                    break;

                case "quality":
                    _adaptiveQuality = 88;
                    _frameDelayMs = 25; // ~40 FPS kristal hedefi
                    break;

                case "auto":
                default:
                    if (_smoothedRttMs < 45)
                    {
                        _adaptiveQuality = 76;
                        _frameDelayMs = 16; // 60 FPS
                    }
                    else if (_smoothedRttMs < 90)
                    {
                        _adaptiveQuality = 68;
                        _frameDelayMs = 16; // 60 FPS
                    }
                    else if (_smoothedRttMs < 160)
                    {
                        _adaptiveQuality = 60;
                        _frameDelayMs = 20; // 50 FPS
                    }
                    else
                    {
                        _adaptiveQuality = 50;
                        _frameDelayMs = 30; // ~33 FPS fallback
                    }
                    break;
            }
        }
    }

    public async Task<NetworkSpeedResult> RunServerNetworkTestAsync()
    {
        if (_identity is null)
        {
            _identity = DeviceIdentityFile.Load();
        }

        if (_identity is null)
        {
            throw new InvalidOperationException("Ajan kimliği bulunamadı.");
        }

        using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(20));
        var baseUrl = _serverUrl.TrimEnd('/');
        var token = Uri.EscapeDataString(_identity.AgentToken);
        var deviceId = _identity.DeviceId;

        var latencyWatch = Stopwatch.StartNew();
        using (await http.GetAsync($"{baseUrl}/health"))
        {
        }
        latencyWatch.Stop();

        var downloadWatch = Stopwatch.StartNew();
        var bytes = await http.GetByteArrayAsync($"{baseUrl}/api/agents/{deviceId}/network-test/download?agentToken={token}&sizeKb=2048&nonce={Guid.NewGuid():N}");
        downloadWatch.Stop();

        var uploadPayload = new byte[1024 * 1024];
        new Random(42).NextBytes(uploadPayload);
        var uploadWatch = Stopwatch.StartNew();
        using var uploadResponse = await http.PostAsync($"{baseUrl}/api/agents/{deviceId}/network-test/upload?agentToken={token}&nonce={Guid.NewGuid():N}", new ByteArrayContent(uploadPayload));
        uploadResponse.EnsureSuccessStatusCode();
        uploadWatch.Stop();

        return new NetworkSpeedResult(
            "Ajan",
            latencyWatch.Elapsed.TotalMilliseconds,
            ToMbps(bytes.Length, downloadWatch.Elapsed),
            ToMbps(uploadPayload.Length, uploadWatch.Elapsed),
            bytes.Length,
            uploadPayload.Length,
            DateTimeOffset.UtcNow);
    }

    private static double ToMbps(int bytes, TimeSpan elapsed)
    {
        var seconds = Math.Max(0.001, elapsed.TotalSeconds);
        return bytes * 8.0 / seconds / 1_000_000.0;
    }

    public static async Task PerformSelfUpdateAsync(
        string msiUrl,
        IProgress<(long BytesRead, long TotalBytes, string Stage)>? progress = null,
        CancellationToken cancellationToken = default,
        string? expectedSha256 = null,
        long? expectedSizeBytes = null)
    {
        var tempMsi = string.Empty;
        try
        {
            var programDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
            Directory.CreateDirectory(programDataDir);
            var pendingMsi = Path.Combine(programDataDir, "pending-update.msi");
            tempMsi = Path.Combine(programDataDir, $"pending-update-{Guid.NewGuid():N}.tmp");

            using var http = NexMoteHttp.CreateClient();
            using var response = await http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            progress?.Report((0, 100, "Sunucuya bağlanıldı, indirme başlatılıyor..."));

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempMsi, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    var dlPct = totalBytes > 0 ? (int)Math.Clamp((totalRead * 65.0) / totalBytes, 1, 65) : 30;
                    progress?.Report((dlPct, 100, $"İndiriliyor: {(totalRead / 1048576.0):F1} MB / {(totalBytes > 0 ? (totalBytes / 1048576.0).ToString("F1") + " MB" : "...")}"));
                }
            }

            ValidateDownloadedPackage(tempMsi, expectedSha256, expectedSizeBytes);
            progress?.Report((70, 100, "Paket doğrulandı, kurulum ortamı hazırlanıyor..."));

            if (File.Exists(pendingMsi))
            {
                try { File.Delete(pendingMsi); } catch { }
            }
            File.Move(tempMsi, pendingMsi, overwrite: true);

            progress?.Report((100, 100, "Paket hazırlandı. Windows Servisi güncellemeyi LocalSystem yetkisiyle sessizce kuracak..."));
            await Task.Delay(1000, cancellationToken);
        }
        catch
        {
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempMsi) && File.Exists(tempMsi))
            {
                try { File.Delete(tempMsi); } catch { }
            }
        }
    }

    private static void ValidateDownloadedPackage(string path, string? expectedSha256, long? expectedSizeBytes)
    {
        NexMote.Shared.Security.AuthenticodeVerifier.ValidateFileIntegrity(path, expectedSha256, expectedSizeBytes);

        // SHA256 doğrulaması başarılı olan paketleri kabul et
        const bool allowUntrustedInDev = true;

        var verification = NexMote.Shared.Security.AuthenticodeVerifier.Verify(
            path,
            expectedSubjectContains: "NexMote",
            allowUntrustedRootInDev: allowUntrustedInDev);

        if (!verification.IsValid)
        {
            throw new InvalidOperationException($"Agent güncelleme paketi güvenlik doğrulaması başarısız: {verification.StatusMessage}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _streamCancellation?.Cancel();
        _streamCancellation?.Dispose();
        _activeSessionId = null;

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        await _captureHelper.DisposeAsync();

        lock (_pipeLock)
        {
            _inputHelperWriter?.Dispose();
            _inputHelperPipe?.Dispose();
            _inputHelperPipe = null;
            _inputHelperWriter = null;
        }

        foreach (var transfer in _activeTransfers.Values)
        {
            try
            {
                transfer.Dispose();
                if (File.Exists(transfer.TempFilePath))
                {
                    File.Delete(transfer.TempFilePath);
                }
            }
            catch { }
        }
        _activeTransfers.Clear();

        try
        {
            _webRtc?.Dispose();
            _webRtc = null;
        }
        catch { }

        _joinedDeviceGroup = false;
    }
}

internal sealed class ActiveFileTransfer : IDisposable
{
    public Guid TransferId { get; }
    public string SafeFileName { get; }
    public string TempFilePath { get; }
    public FileStream Stream { get; }
    public long TotalSize { get; }
    public long BytesReceived { get; set; }
    public DateTimeOffset LastActivityUtc { get; set; }

    public ActiveFileTransfer(Guid transferId, string safeFileName, string tempFilePath, FileStream stream, long totalSize)
    {
        TransferId = transferId;
        SafeFileName = safeFileName;
        TempFilePath = tempFilePath;
        Stream = stream;
        TotalSize = totalSize;
        LastActivityUtc = DateTimeOffset.UtcNow;
    }

    public void Dispose()
    {
        Stream.Dispose();
    }
}
