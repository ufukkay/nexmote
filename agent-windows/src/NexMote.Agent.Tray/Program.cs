using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.ServiceProcess;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;

namespace NexMote.Agent.Tray;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ServiceName = "NexMote Agent";
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _serverItem;
    private readonly ToolStripMenuItem _screenItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _signalingTimer;
    private readonly SynchronizationContext? _uiContext;
    private readonly RemoteScreenStreamer _streamer;
    private readonly string _serverUrl;

    public TrayApplicationContext()
    {
        _uiContext = SynchronizationContext.Current;
        _serverUrl = AgentSettings.LoadServerUrl();
        _statusItem = new ToolStripMenuItem("Servis durumu: kontrol ediliyor") { Enabled = false };
        _serverItem = new ToolStripMenuItem($"Sunucu: {_serverUrl}") { Enabled = false };
        _screenItem = new ToolStripMenuItem("Goruntu akisi: hazirlaniyor") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add("NexMote Agent").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_serverItem);
        menu.Items.Add(_screenItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Paneli Ac", null, (_, _) => OpenWebPanel());
        menu.Items.Add("Durumu Yenile", null, (_, _) => RefreshStatus(showBalloon: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tray'i Kapat", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "NexMote Agent",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (_, _) => ShowStatusMessage();

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 15000
        };
        _timer.Tick += (_, _) => RefreshStatus(showBalloon: false);
        _timer.Start();

        _streamer = new RemoteScreenStreamer(_serverUrl, UpdateScreenStatus);
        _signalingTimer = new System.Windows.Forms.Timer
        {
            Interval = 10000
        };
        _signalingTimer.Tick += async (_, _) => await _streamer.EnsureStartedAsync();
        _signalingTimer.Start();

        RefreshStatus(showBalloon: false);
        _ = _streamer.EnsureStartedAsync();
    }

    private void RefreshStatus(bool showBalloon)
    {
        var status = GetServiceStatus();
        _statusItem.Text = $"Servis durumu: {status}";
        _notifyIcon.Text = $"NexMote Agent - {status}";

        if (showBalloon)
        {
            _notifyIcon.BalloonTipTitle = "NexMote Agent";
            _notifyIcon.BalloonTipText = $"Servis durumu: {status}";
            _notifyIcon.ShowBalloonTip(2500);
        }
    }

    private static string GetServiceStatus()
    {
        try
        {
            using var controller = new ServiceController(ServiceName);
            return controller.Status switch
            {
                ServiceControllerStatus.Running => "Calisiyor",
                ServiceControllerStatus.Stopped => "Durdu",
                ServiceControllerStatus.Paused => "Duraklatildi",
                ServiceControllerStatus.StartPending => "Baslatiliyor",
                ServiceControllerStatus.StopPending => "Durduruluyor",
                _ => controller.Status.ToString()
            };
        }
        catch
        {
            return Process.GetProcessesByName("NexMote.Agent.Windows").Length > 0
                ? "Calisiyor (test)"
                : "Bulunamadi";
        }
    }

    private void ShowStatusMessage()
    {
        RefreshStatus(showBalloon: false);
        MessageBox.Show(
            $"NexMote Agent\n\n{_statusItem.Text}\n{_serverItem.Text}\n{_screenItem.Text}",
            "NexMote Agent",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateScreenStatus(string status)
    {
        void Apply()
        {
            _screenItem.Text = $"Goruntu akisi: {status}";
        }

        if (_uiContext is null)
        {
            Apply();
            return;
        }

        _uiContext.Post(_ => Apply(), null);
    }

    private void OpenWebPanel()
    {
        try
        {
            var uri = new Uri(_serverUrl);
            var panelUri = $"{uri.Scheme}://{uri.Host}:5173/";
            Process.Start(new ProcessStartInfo(panelUri) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Panel adresi acilamadi.", "NexMote Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _signalingTimer.Stop();
        _timer.Dispose();
        _signalingTimer.Dispose();
        _streamer.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class RemoteScreenStreamer : IAsyncDisposable
{
    private readonly string _serverUrl;
    private readonly Action<string> _setStatus;
    private HubConnection? _connection;
    private DeviceIdentity? _identity;
    private CancellationTokenSource? _streamCancellation;
    private Guid? _activeSessionId;
    private bool _starting;
    private bool _disposed;
    private bool _joinedDeviceGroup;

    public RemoteScreenStreamer(string serverUrl, Action<string> setStatus)
    {
        _serverUrl = serverUrl;
        _setStatus = setStatus;
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
            _setStatus("identity bekleniyor");
            return;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _joinedDeviceGroup = false;
        }

        var hubUrl = $"{_serverUrl.TrimEnd('/')}/hubs/signaling";
        _connection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _connection.On<Guid>("RemoteSessionRequested", sessionId =>
        {
            _ = HandleRemoteSessionRequestedAsync(sessionId);
        });

        _connection.On<string, string>("SignalReceived", (type, payload) =>
        {
            if (string.Equals(type, "remote-input", StringComparison.OrdinalIgnoreCase))
            {
                HandleRemoteInput(payload);
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

    private async Task JoinDeviceAsync()
    {
        if (_connection is null || _identity is null)
        {
            return;
        }

        await _connection.InvokeAsync("JoinDevice", _identity.DeviceId, _identity.AgentToken);
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
        _ = Task.Run(() => StreamLoopAsync(sessionId, _streamCancellation.Token));
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
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(payload);
            if (input is null || _activeSessionId != input.SessionId)
            {
                return;
            }

            switch (input.Kind.ToLowerInvariant())
            {
                case "mouse-move":
                    InputInjector.MoveMouse(input.X, input.Y);
                    break;
                case "mouse-button":
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
        catch (Exception ex)
        {
            _setStatus($"input uygulanamadi ({ex.Message})");
        }
    }

    private async Task StreamLoopAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        _setStatus("goruntu gonderiliyor");

        while (!cancellationToken.IsCancellationRequested && _connection?.State == HubConnectionState.Connected)
        {
            try
            {
                var frame = ScreenCapture.CaptureJpegBase64();
                await _connection.InvokeAsync("SendSignal", sessionId, "screen-frame", frame, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(750), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _setStatus($"frame hatasi ({ex.Message})");
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
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

        _joinedDeviceGroup = false;
    }
}

internal static class DeviceIdentityFile
{
    public static DeviceIdentity? Load()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var identityPath = Path.Combine(programData, "NexMote", "Agent", "identity.json");
        if (!File.Exists(identityPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<DeviceIdentity>(File.ReadAllText(identityPath));
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record DeviceIdentity(Guid DeviceId, string AgentToken);

internal static class ScreenCapture
{
    public static RemoteScreenInfo GetInfo()
    {
        var bounds = SystemInformation.VirtualScreen;
        return new RemoteScreenInfo(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
    }

    public static string CaptureJpegBase64()
    {
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Aktif ekran bulunamadi.");
        }

        using var capture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(capture))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        using var resized = ResizeIfNeeded(capture, 1280);
        using var stream = new MemoryStream();
        SaveJpeg(resized, stream, 45L);
        return Convert.ToBase64String(stream.ToArray());
    }

    private static Bitmap ResizeIfNeeded(Bitmap source, int maxWidth)
    {
        if (source.Width <= maxWidth)
        {
            return (Bitmap)source.Clone();
        }

        var height = Math.Max(1, (int)Math.Round(source.Height * (maxWidth / (double)source.Width)));
        var resized = new Bitmap(maxWidth, height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(resized);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(source, 0, 0, maxWidth, height);
        return resized;
    }

    private static void SaveJpeg(Bitmap image, Stream stream, long quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders().FirstOrDefault(item => item.MimeType == "image/jpeg");
        if (codec is null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(stream, codec, parameters);
    }
}

internal static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheelFlag = 0x0800;
    private const uint KeyboardKeyUp = 0x0002;

    public static void MoveMouse(int x, int y)
    {
        var bounds = SystemInformation.VirtualScreen;
        var clampedX = Math.Clamp(x, bounds.Left, bounds.Right - 1);
        var clampedY = Math.Clamp(y, bounds.Top, bounds.Bottom - 1);
        SetCursorPos(clampedX, clampedY);
    }

    public static void MouseButton(string? button, bool isDown)
    {
        var flags = (button?.ToLowerInvariant(), isDown) switch
        {
            ("left", true) => MouseLeftDown,
            ("left", false) => MouseLeftUp,
            ("right", true) => MouseRightDown,
            ("right", false) => MouseRightUp,
            ("middle", true) => MouseMiddleDown,
            ("middle", false) => MouseMiddleUp,
            _ => 0u
        };

        if (flags != 0)
        {
            SendMouse(flags, 0);
        }
    }

    public static void MouseWheel(int delta)
    {
        if (delta != 0)
        {
            SendMouse(MouseWheelFlag, delta);
        }
    }

    public static void Keyboard(int keyCode, bool isDown)
    {
        if (keyCode is <= 0 or > ushort.MaxValue)
        {
            return;
        }

        var input = new INPUT
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)keyCode,
                    Flags = isDown ? 0u : KeyboardKeyUp
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void SendMouse(uint flags, int mouseData)
    {
        var input = new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                Mouse = new MOUSEINPUT
                {
                    Flags = flags,
                    MouseData = mouseData
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public MOUSEINPUT Mouse;

        [FieldOffset(0)]
        public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}

internal static class AgentSettings
{
    public static string LoadServerUrl()
    {
        var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(configPath))
        {
            return "http://127.0.0.1:5080";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            return document.RootElement
                .GetProperty("Agent")
                .GetProperty("ServerUrl")
                .GetString() ?? "http://127.0.0.1:5080";
        }
        catch
        {
            return "http://127.0.0.1:5080";
        }
    }
}
