using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;

namespace NexMote.TechnicianApp;

public partial class MainWindow : Window
{
    private HubConnection? _connection;
    private Guid? _sessionId;
    private RemoteScreenInfo? _screenInfo;
    private long _lastMouseMoveTimestamp;
    private Point _lastRemotePoint;

    public MainWindow()
    {
        InitializeComponent();
        ParseLaunchArguments();
    }

    private void ParseLaunchArguments()
    {
        var args = Environment.GetCommandLineArgs();
        var launchUri = args.Skip(1).FirstOrDefault(value => value.StartsWith("nexmote://", StringComparison.OrdinalIgnoreCase));
        if (launchUri is null)
        {
            return;
        }

        var uri = new Uri(launchUri);
        var query = ParseQuery(uri.Query);
        query.TryGetValue("sessionId", out var sessionId);
        query.TryGetValue("token", out var token);
        query.TryGetValue("serverUrl", out var serverUrl);

        SessionText.Text = $"Session: {sessionId}";
        if (!Guid.TryParse(sessionId, out var parsedSessionId) || string.IsNullOrWhiteSpace(token))
        {
            StatusText.Text = "Session veya token bulunamadi.";
            return;
        }

        _sessionId = parsedSessionId;
        _ = ConnectSignalingAsync(parsedSessionId, token, string.IsNullOrWhiteSpace(serverUrl) ? "http://127.0.0.1:5080" : serverUrl);
    }

    private async Task ConnectSignalingAsync(Guid sessionId, string token, string serverUrl)
    {
        try
        {
            StatusText.Text = "Signaling baglantisi kuruluyor.";
            var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/signaling";
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On("DeviceJoinedSession", () =>
            {
                Dispatcher.Invoke(() => StatusText.Text = "Agent baglandi, goruntu bekleniyor.");
            });

            _connection.On<string, string>("SignalReceived", (type, payload) =>
            {
                if (string.Equals(type, "screen-info", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => UpdateScreenInfo(payload));
                    return;
                }

                if (!string.Equals(type, "screen-frame", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Dispatcher.Invoke(() => ShowFrame(payload));
            });

            _connection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() => StatusText.Text = $"Signaling yeniden baglaniyor: {error?.Message}");
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                Dispatcher.Invoke(() => StatusText.Text = "Signaling tekrar baglandi.");
                return _connection.InvokeAsync("JoinTechnicianSession", sessionId, token);
            };

            _connection.Closed += error =>
            {
                Dispatcher.Invoke(() => StatusText.Text = $"Signaling kapandi: {error?.Message ?? "baglanti kapandi"}");
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            await _connection.InvokeAsync("JoinTechnicianSession", sessionId, token);
            StatusText.Text = "Session'a baglanildi, agent goruntusu bekleniyor.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Baglanti hatasi: {ex.Message}";
        }
    }

    private void UpdateScreenInfo(string payload)
    {
        try
        {
            _screenInfo = JsonSerializer.Deserialize<RemoteScreenInfo>(payload);
            if (_screenInfo is not null)
            {
                StatusText.Text = $"Uzak ekran: {_screenInfo.Width}x{_screenInfo.Height}";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ekran bilgisi okunamadi: {ex.Message}";
        }
    }

    private void ShowFrame(string base64Jpeg)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Jpeg);
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            RemoteImage.Source = image;
            PlaceholderText.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Goruntu aliniyor: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Frame okunamadi: {ex.Message}";
        }
    }

    private void RemoteSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!TryMapToRemote(e.GetPosition(RemoteSurface), out var x, out var y))
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var minimumTicks = Stopwatch.Frequency / 30;
        var moved = Math.Abs(x - _lastRemotePoint.X) >= 2 || Math.Abs(y - _lastRemotePoint.Y) >= 2;
        if (_lastMouseMoveTimestamp != 0 && now - _lastMouseMoveTimestamp < minimumTicks && !moved)
        {
            return;
        }

        _lastMouseMoveTimestamp = now;
        _lastRemotePoint = new Point(x, y);
        _ = SendRemoteInputAsync(new RemoteInputEvent(_sessionId ?? Guid.Empty, "mouse-move", x, y));
    }

    private void RemoteSurface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HandleMouseButton(e, "left", true);
        e.Handled = true;
    }

    private void RemoteSurface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        HandleMouseButton(e, "left", false);
        e.Handled = true;
    }

    private void RemoteSurface_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        HandleMouseButton(e, "right", true);
        e.Handled = true;
    }

    private void RemoteSurface_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        HandleMouseButton(e, "right", false);
        e.Handled = true;
    }

    private void RemoteSurface_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HandleMouseButton(e, "middle", true);
            e.Handled = true;
        }
    }

    private void RemoteSurface_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle)
        {
            HandleMouseButton(e, "middle", false);
            e.Handled = true;
        }
    }

    private void HandleMouseButton(MouseButtonEventArgs e, string button, bool isDown)
    {
        if (!TryMapToRemote(e.GetPosition(RemoteSurface), out var x, out var y))
        {
            // A captured mouse can be released outside the rendered image. Reuse the
            // last valid point so the remote button state is always released.
            if (isDown || !RemoteSurface.IsMouseCaptured || _screenInfo is null)
            {
                return;
            }

            x = (int)Math.Round(_lastRemotePoint.X);
            y = (int)Math.Round(_lastRemotePoint.Y);
        }

        RemoteSurface.Focus();
        _lastRemotePoint = new Point(x, y);
        if (isDown)
        {
            RemoteSurface.CaptureMouse();
        }
        else if (RemoteSurface.IsMouseCaptured)
        {
            RemoteSurface.ReleaseMouseCapture();
        }

        _ = SendRemoteInputAsync(new RemoteInputEvent(
            _sessionId ?? Guid.Empty,
            "mouse-button",
            x,
            y,
            button,
            isDown));
    }

    private void RemoteSurface_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        RemoteSurface.Focus();
        _ = SendRemoteInputAsync(new RemoteInputEvent(
            _sessionId ?? Guid.Empty,
            "mouse-wheel",
            WheelDelta: e.Delta));
    }

    private void RemoteSurface_KeyDown(object sender, KeyEventArgs e)
    {
        SendKey(e, true);
        e.Handled = true;
    }

    private void RemoteSurface_KeyUp(object sender, KeyEventArgs e)
    {
        SendKey(e, false);
        e.Handled = true;
    }

    private void SendKey(KeyEventArgs e, bool isDown)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var keyCode = KeyInterop.VirtualKeyFromKey(key);
        if (keyCode == 0)
        {
            return;
        }

        _ = SendRemoteInputAsync(new RemoteInputEvent(
            _sessionId ?? Guid.Empty,
            "key",
            KeyCode: keyCode,
            IsDown: isDown));
    }

    private async Task SendRemoteInputAsync(RemoteInputEvent input)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(input);
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "remote-input", payload);
        }
        catch (Exception ex)
        {
            await Dispatcher.BeginInvoke(() => StatusText.Text = $"Input gonderilemedi: {ex.Message}");
        }
    }

    private bool TryMapToRemote(Point surfacePoint, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (_screenInfo is null || RemoteSurface.ActualWidth <= 0 || RemoteSurface.ActualHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(
            RemoteSurface.ActualWidth / _screenInfo.Width,
            RemoteSurface.ActualHeight / _screenInfo.Height);
        var renderedWidth = _screenInfo.Width * scale;
        var renderedHeight = _screenInfo.Height * scale;
        var offsetX = (RemoteSurface.ActualWidth - renderedWidth) / 2;
        var offsetY = (RemoteSurface.ActualHeight - renderedHeight) / 2;
        var imageX = surfacePoint.X - offsetX;
        var imageY = surfacePoint.Y - offsetY;

        if (imageX < 0 || imageY < 0 || imageX > renderedWidth || imageY > renderedHeight)
        {
            return false;
        }

        x = _screenInfo.Left + (int)Math.Round(imageX / scale);
        y = _screenInfo.Top + (int)Math.Round(imageY / scale);
        return true;
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        base.OnClosed(e);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.OrdinalIgnoreCase);
    }
}
