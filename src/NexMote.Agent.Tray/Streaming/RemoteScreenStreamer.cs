using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
using NexMote.Shared.Network;

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
    private AgentSecurityProfileResponse? _securityProfile;
    private ConnectionBannerForm? _bannerForm;
    private volatile string? _lastClipboardText;
    private int _adaptiveQuality = 72;
    private readonly object _qualityLock = new();
    private readonly ConcurrentDictionary<int, long> _lastAckedSequencePerDisplay = new();
    private readonly Dictionary<Guid, (MemoryStream Stream, string FileName)> _activeTransfers = new();
    private NamedPipeClientStream? _inputHelperPipe;
    private StreamWriter? _inputHelperWriter;
    private long _nextPipeConnectAttemptTicks;
    private readonly object _pipeLock = new();

    private readonly Func<Task>? _onSecurityProfileUpdated;

    public RemoteScreenStreamer(string serverUrl, Action<string> setStatus, Func<Task>? onSecurityProfileUpdated = null)
    {
        _serverUrl = serverUrl;
        _setStatus = setStatus;
        _onSecurityProfileUpdated = onSecurityProfileUpdated;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

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
        if (_disposed || _starting || (_connection?.State == HubConnectionState.Connected && _joinedDeviceGroup))
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
            await _connection.DisposeAsync();
            _joinedDeviceGroup = false;
        }

        var hubUrl = $"{_serverUrl.TrimEnd('/')}/hubs/signaling";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.HttpMessageHandlerFactory = _ => NexMoteHttp.CreateHandler();
            })
            .WithAutomaticReconnect()
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
                    return; // Sadece izleme modu aktif
                }
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
            else if (string.Equals(type, "refresh-screen", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 1; i <= ScreenCapture.GetDisplayCount(); i++)
                {
                    ScreenCapture.ResetHash(i);
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

        _connection.Reconnecting += error =>
        {
            _joinedDeviceGroup = false;
            _setStatus($"yeniden baglaniyor ({error?.Message})");
            return Task.CompletedTask;
        };

        _connection.Reconnected += async _ =>
        {
            await JoinDeviceAsync();
            _joinedDeviceGroup = true;
            if (_activeSessionId.HasValue && _identity is not null)
            {
                try
                {
                    await _connection.InvokeAsync("JoinDeviceSession", _activeSessionId.Value, _identity.DeviceId, _identity.AgentToken);
                    StartStreaming(_activeSessionId.Value);
                }
                catch { }
            }
            _setStatus("hazir");
        };

        _connection.Closed += error =>
        {
            _joinedDeviceGroup = false;
            _setStatus($"kapandi ({error?.Message ?? "baglanti kapandi"})");
            return Task.CompletedTask;
        };

        await _connection.StartAsync();
        try
        {
            await JoinDeviceAsync();
            _joinedDeviceGroup = true;
            _setStatus("hazir");
        }
        catch
        {
            _joinedDeviceGroup = false;
            await _connection.DisposeAsync();
            _connection = null;
            throw;
        }
    }

    private async Task HandleConsentRequestAsync(ConnectionConsentRequest req)
    {
        if (_connection is null || _identity is null) return;
        bool accepted = false;
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
            return;
        }

        try
        {
            await _connection.InvokeAsync("JoinDevice", _identity.DeviceId, _identity.AgentToken);
        }
        catch (Exception)
        {
            try
            {
                var enrollKey = AgentSettings.LoadEnrollmentKey();
                var refreshed = await DeviceIdentityFile.EnsureEnrolledAsync(_serverUrl, enrollKey);
                if (refreshed is not null)
                {
                    _identity = refreshed;
                    await _connection.InvokeAsync("JoinDevice", _identity.DeviceId, _identity.AgentToken);
                }
            }
            catch { }
        }
    }

    private async Task HandleRemoteSessionRequestedAsync(Guid sessionId)
    {
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
        _ = SendScreenInfoAsync(sessionId);

        if (_securityProfile?.ShowConnectionBanner != false)
        {
            ShowBanner();
        }

        var token = _streamCancellation.Token;
        token.Register(() => HideBanner());
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

    private void ShowBanner()
    {
        try
        {
            if (_bannerForm != null && !_bannerForm.IsDisposed) return;
            var thread = new Thread(() =>
            {
                try
                {
                    var title = _securityProfile?.AgentDisplayName ?? "NexMote";
                    _bannerForm = new ConnectionBannerForm(title);
                    Application.Run(_bannerForm);
                }
                catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }
        catch { }
    }

    private void HideBanner()
    {
        try
        {
            if (_bannerForm != null && !_bannerForm.IsDisposed)
            {
                _bannerForm.Invoke(() => _bannerForm.Close());
                _bannerForm = null;
            }
        }
        catch { }
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
                var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var rtt = Math.Max(0, now - ack.ReceivedAtUnixMs);
                AdjustQuality(rtt);
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
                    _inputHelperPipe.Connect(25);
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
                _nextPipeConnectAttemptTicks = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 2);
                return false;
            }
        }
    }

    private void HandleFileChunk(string payload)
    {
        try
        {
            var chunk = JsonSerializer.Deserialize<FileTransferChunk>(payload);
            if (chunk is null || _activeSessionId != chunk.SessionId)
            {
                return;
            }

            if (!_activeTransfers.TryGetValue(chunk.TransferId, out var state))
            {
                state = (new MemoryStream(), chunk.FileName);
                _activeTransfers[chunk.TransferId] = state;
            }

            var bytes = Convert.FromBase64String(chunk.Base64Data);
            state.Stream.Write(bytes, 0, bytes.Length);
            _setStatus($"dosya aliniyor: {state.FileName} ({chunk.ChunkIndex + 1}/{chunk.TotalChunks})");

            if (chunk.IsLast)
            {
                _activeTransfers.Remove(chunk.TransferId);
                SaveIncomingFile(state.FileName, state.Stream.ToArray());
                state.Stream.Dispose();
            }
        }
        catch (Exception ex)
        {
            _setStatus($"dosya alinamadi ({ex.Message})");
        }
    }

    private void SaveIncomingFile(string fileName, byte[] data)
    {
        try
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var incomingDir = Path.Combine(programData, "NexMote", "Agent", "Incoming");
            Directory.CreateDirectory(incomingDir);

            var safeName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "dosya.bin";
            }

            var targetPath = Path.Combine(incomingDir, safeName);
            if (File.Exists(targetPath))
            {
                var ext = Path.GetExtension(safeName);
                var baseName = Path.GetFileNameWithoutExtension(safeName);
                targetPath = Path.Combine(incomingDir, $"{baseName}_{DateTime.Now:HHmmss}{ext}");
            }

            File.WriteAllBytes(targetPath, data);
            _setStatus($"dosya alindi: {Path.GetFileName(targetPath)}");
        }
        catch (Exception ex)
        {
            _setStatus($"dosya kaydedilemedi ({ex.Message})");
        }
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
            if (_connection is null || _connection.State != HubConnectionState.Connected)
            {
                var reconnected = false;
                for (int i = 0; i < 20; i++)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    await Task.Delay(500, cancellationToken);
                    if (_connection?.State == HubConnectionState.Connected)
                    {
                        reconnected = true;
                        ScreenCapture.ResetHash(displayIndex);
                        break;
                    }
                }
                if (!reconnected) break;
            }

            try
            {
                var lastAcked = _lastAckedSequencePerDisplay.GetValueOrDefault(displayIndex, 0);
                if (sequence > 0 && lastAcked < sequence)
                {
                    var waitLimitMs = _selectedQualityMode switch
                    {
                        "speed" => 35,
                        "quality" => 120,
                        "balanced" => 70,
                        _ => Math.Clamp(_smoothedRttMs + 35, 40, 85)
                    };

                    var waitStart = Stopwatch.GetTimestamp();
                    while (sequence > _lastAckedSequencePerDisplay.GetValueOrDefault(displayIndex, 0))
                    {
                        var elapsedWaitMs = (Stopwatch.GetTimestamp() - waitStart) * 1000 / Stopwatch.Frequency;
                        if (elapsedWaitMs >= waitLimitMs || cancellationToken.IsCancellationRequested)
                        {
                            AdjustQuality(120);
                            break;
                        }
                        await Task.Delay(3, cancellationToken);
                    }
                }

                var now = Stopwatch.GetTimestamp();
                var forceSend = (initialBurst > 0) || (now - lastSendTicks) >= forceIntervalTicks;
                if (initialBurst > 0)
                {
                    initialBurst--;
                    ScreenCapture.ResetHash(displayIndex);
                }
                else if (forceSend)
                {
                    ScreenCapture.ResetHash(displayIndex);
                    refinementSent = false;
                }

                var timeSinceMotionMs = (now - lastMotionTicks) * 1000 / Stopwatch.Frequency;
                var isRefinement = !refinementSent && timeSinceMotionMs > 150 && (now - lastSendTicks) > 0;

                int quality;
                if (isRefinement)
                {
                    quality = 92;
                    forceSend = true;
                }
                else
                {
                    quality = Math.Clamp(GetCurrentQuality(), 48, 92);
                }

                var frame = ScreenCapture.CaptureJpegBase64(displayIndex, quality, forceSend);

                if (frame is not null && _connection?.State == HubConnectionState.Connected)
                {
                    sequence++;
                    var payload = JsonSerializer.Serialize(new MultiScreenFrame(
                        displayIndex,
                        JpegBase64: frame,
                        Sequence: sequence,
                        CapturedAtUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

                    var sendStopwatch = Stopwatch.StartNew();
                    await _connection.InvokeAsync("SendSignal", sessionId, "screen-frame-multi", payload, cancellationToken);
                    sendStopwatch.Stop();

                    if (!isRefinement)
                    {
                        AdjustQuality(sendStopwatch.ElapsedMilliseconds);
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
            _smoothedRttMs = (int)Math.Max(1, rttMs);

            switch (_selectedQualityMode)
            {
                case "speed":
                    _adaptiveQuality = 58;
                    _frameDelayMs = 16;
                    break;

                case "balanced":
                    _adaptiveQuality = 74;
                    _frameDelayMs = 25;
                    break;

                case "quality":
                    _adaptiveQuality = 92;
                    _frameDelayMs = 33;
                    break;

                case "auto":
                default:
                    if (_smoothedRttMs < 30)
                    {
                        _adaptiveQuality = 84;
                        _frameDelayMs = 16;
                    }
                    else if (_smoothedRttMs < 75)
                    {
                        _adaptiveQuality = 74;
                        _frameDelayMs = 22;
                    }
                    else if (_smoothedRttMs < 140)
                    {
                        _adaptiveQuality = 60;
                        _frameDelayMs = 33;
                    }
                    else
                    {
                        _adaptiveQuality = 48;
                        _frameDelayMs = 50;
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
        CancellationToken cancellationToken = default)
    {
        try
        {
            var programDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
            Directory.CreateDirectory(programDataDir);
            var pendingMsi = Path.Combine(programDataDir, "pending-update.msi");
            var tempMsi = Path.Combine(programDataDir, "pending-update.tmp");

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

            progress?.Report((70, 100, "Paket doğrulandı, kurulum ortamı hazırlanıyor..."));
            if (File.Exists(pendingMsi))
            {
                try { File.Delete(pendingMsi); } catch { }
            }
            File.Move(tempMsi, pendingMsi, overwrite: true);

            progress?.Report((75, 100, "Kurulum başlatıldı, sistem dosyaları güncelleniyor..."));

            var logPath = Path.Combine(programDataDir, "update.log");
            Process? installerProc = null;
            try
            {
                var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{pendingMsi}\" /qn /norestart /l*v \"{logPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                installerProc = Process.Start(psi);
            }
            catch
            {
                try
                {
                    var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{pendingMsi}\" /qn /norestart /l*v \"{logPath}\"")
                    {
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    installerProc = Process.Start(psi);
                }
                catch { }
            }

            var installPct = 75;
            var maxWaitSeconds = 60;
            var startWait = Stopwatch.GetTimestamp();

            while ((Stopwatch.GetTimestamp() - startWait) / Stopwatch.Frequency < maxWaitSeconds)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (installerProc != null && installerProc.HasExited)
                {
                    break;
                }

                if (!File.Exists(pendingMsi))
                {
                    break;
                }

                installPct = Math.Min(96, installPct + 3);
                progress?.Report((installPct, 100, $"Kuruluyor (%{installPct})... Sistem dosyaları yenileniyor"));

                await Task.Delay(1000, cancellationToken);
            }

            progress?.Report((100, 100, "✓ Kurulum başarıyla tamamlandı! Ajan yenileniyor..."));
            await Task.Delay(1500, cancellationToken);
        }
        catch
        {
            throw;
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

        lock (_pipeLock)
        {
            _inputHelperWriter?.Dispose();
            _inputHelperPipe?.Dispose();
            _inputHelperPipe = null;
            _inputHelperWriter = null;
        }

        _joinedDeviceGroup = false;
    }
}
