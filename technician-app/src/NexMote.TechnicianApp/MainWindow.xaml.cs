using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;

namespace NexMote.TechnicianApp;

public partial class MainWindow : Window
{
    private readonly HttpClient _http = new();
    private string _serverUrl = "http://192.168.0.104:5080";
    private HubConnection? _connection;
    private Guid? _sessionId;
    private RemoteScreenInfo? _screenInfo;
    private long _lastMouseMoveTimestamp;
    private Point _lastRemotePoint;

    // Scale mode: 0 = Fit (Uniform), 1 = Native (1:1), 2 = Stretch (Fill)
    private int _scaleMode = 0;

    // Quality mode: 0 = Dengeli (55%), 1 = Yüksek (75%), 2 = Hızlı (35%)
    private int _qualityMode = 0;
    private readonly int[] _qualityValues = [55, 75, 35];
    private readonly string[] _qualityNames = ["⚡ Kalite: Dengeli", "🌟 Kalite: Yüksek", "🚀 Kalite: Hızlı"];

    // Multi-Monitor active index
    private int _activeDisplayIndex = 0;

    // Performance & Stats metrics
    private int _frameCount;
    private long _bytesReceived;
    private long _lastStatsTimestamp = Stopwatch.GetTimestamp();

    // RTT Ping metrics
    private System.Windows.Threading.DispatcherTimer? _pingTimer;
    private long _pingSentTimestamp;

    public MainWindow()
    {
        InitializeComponent();
        var launchedSession = ParseLaunchArguments();
        if (!launchedSession)
        {
            SwitchToDeviceList();
            _ = LoadDevicesAsync();
        }
    }

    private void SwitchToDeviceList()
    {
        DeviceInventoryGrid.Visibility = Visibility.Visible;
        RemoteCanvasGrid.Visibility = Visibility.Collapsed;
        RemoteControlsPanel.Visibility = Visibility.Collapsed;
        MonitorSelectorBorder.Visibility = Visibility.Collapsed;
        SessionText.Text = "Cihaz Seçimi";
        StatusText.Text = "Cihaz listesi yükleniyor...";
    }

    private void SwitchToRemoteSession()
    {
        DeviceInventoryGrid.Visibility = Visibility.Collapsed;
        RemoteCanvasGrid.Visibility = Visibility.Visible;
        RemoteControlsPanel.Visibility = Visibility.Visible;
        MonitorSelectorBorder.Visibility = Visibility.Visible;
    }

    private bool ParseLaunchArguments()
    {
        var args = Environment.GetCommandLineArgs();
        var launchUri = args.Skip(1).FirstOrDefault(value => value.StartsWith("nexmote://", StringComparison.OrdinalIgnoreCase));
        if (launchUri is null)
        {
            return false;
        }

        try
        {
            var uri = new Uri(launchUri);
            var query = ParseQuery(uri.Query);
            query.TryGetValue("sessionId", out var sessionId);
            query.TryGetValue("token", out var token);
            query.TryGetValue("serverUrl", out var serverUrl);

            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                _serverUrl = serverUrl;
            }

            SessionText.Text = $"Oturum: {sessionId}";
            if (!Guid.TryParse(sessionId, out var parsedSessionId) || string.IsNullOrWhiteSpace(token))
            {
                StatusText.Text = "Geçersiz oturum kimliği veya token.";
                return false;
            }

            _sessionId = parsedSessionId;
            SwitchToRemoteSession();
            _ = ConnectSignalingAsync(parsedSessionId, token, _serverUrl);
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Başlatma hatası: {ex.Message}";
            return false;
        }
    }

    private async Task LoadDevicesAsync()
    {
        try
        {
            StatusText.Text = "Cihazlar getiriliyor...";
            var devices = await _http.GetFromJsonAsync<List<DeviceModel>>($"{_serverUrl.TrimEnd('/')}/api/devices");
            if (devices is not null)
            {
                DevicesDataGrid.ItemsSource = devices;
                var onlineCount = devices.Count(d => d.IsOnline);
                StatusText.Text = $"Toplam {devices.Count} cihaz bulundu ({onlineCount} online).";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cihaz listesi alınamadı: {ex.Message}";
        }
    }

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
    {
        await LoadDevicesAsync();
    }

    private async void ConnectToDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid deviceId)
        {
            await InitiateRemoteSessionAsync(deviceId);
        }
    }

    private async void DevicesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DevicesDataGrid.SelectedItem is DeviceModel device)
        {
            await InitiateRemoteSessionAsync(device.Id);
        }
    }

    private async Task InitiateRemoteSessionAsync(Guid deviceId)
    {
        try
        {
            StatusText.Text = "Uzaktan oturum başlatılıyor...";
            var request = new CreateRemoteSessionRequest(deviceId);
            var response = await _http.PostAsJsonAsync($"{_serverUrl.TrimEnd('/')}/api/remote-sessions", request);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                StatusText.Text = $"Oturum başlatılamadı: {err}";
                MessageBox.Show($"Cihaza bağlanılamadı. Cihazın online olduğundan emin olun.\n({err})", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var session = await response.Content.ReadFromJsonAsync<CreateRemoteSessionResponse>();
            if (session is null)
            {
                StatusText.Text = "Oturum yanıtı okunamadı.";
                return;
            }

            _sessionId = session.SessionId;
            SessionText.Text = $"Oturum: {session.SessionId}";
            SwitchToRemoteSession();

            var uri = new Uri(session.LaunchUri);
            var query = ParseQuery(uri.Query);
            query.TryGetValue("token", out var token);
            await ConnectSignalingAsync(session.SessionId, token ?? string.Empty, _serverUrl);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Bağlantı hatası: {ex.Message}";
            MessageBox.Show($"Bağlantı hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ConnectSignalingAsync(Guid sessionId, string token, string serverUrl)
    {
        try
        {
            StatusText.Text = "Sinyalleşme sunucusuna bağlanılıyor...";
            var hubUrl = $"{serverUrl.TrimEnd('/')}/hubs/signaling";
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            _connection.On("DeviceJoinedSession", () =>
            {
                Dispatcher.Invoke(() => StatusText.Text = "Hedef cihaz bağlandı, canlı ekran akışı bekleniyor...");
            });

            _connection.On<string, string>("SignalReceived", (type, payload) =>
            {
                if (string.Equals(type, "screen-info", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => UpdateScreenInfo(payload));
                    return;
                }

                if (string.Equals(type, "pong", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => CalculatePing(payload));
                    return;
                }

                if (string.Equals(type, "screen-frame", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => ShowFrame(payload));
                }
            });

            _connection.Reconnecting += error =>
            {
                Dispatcher.Invoke(() => StatusText.Text = $"Bağlantı kesildi, yeniden bağlanılıyor: {error?.Message}");
                return Task.CompletedTask;
            };

            _connection.Reconnected += _ =>
            {
                Dispatcher.Invoke(() => StatusText.Text = "Sinyalleşme tekrar sağlandı.");
                return _connection.InvokeAsync("JoinTechnicianSession", sessionId, token);
            };

            _connection.Closed += error =>
            {
                Dispatcher.Invoke(() => StatusText.Text = $"Bağlantı kapandı: {error?.Message ?? "oturum sonlandırıldı."}");
                return Task.CompletedTask;
            };

            await _connection.StartAsync();
            await _connection.InvokeAsync("JoinTechnicianSession", sessionId, token);
            StatusText.Text = "Oturuma katılındı. Görüntü akışı bekleniyor...";

            StartPingTimer();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Bağlantı hatası: {ex.Message}";
        }
    }

    private void StartPingTimer()
    {
        _pingTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pingTimer.Tick += async (s, e) =>
        {
            if (_sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
            {
                _pingSentTimestamp = Stopwatch.GetTimestamp();
                try
                {
                    await _connection.InvokeAsync("SendSignal", _sessionId.Value, "ping", _pingSentTimestamp.ToString());
                }
                catch { }
            }
        };
        _pingTimer.Start();
    }

    private void CalculatePing(string payload)
    {
        if (long.TryParse(payload, out var sentTicks))
        {
            var rttMs = Math.Round((double)(Stopwatch.GetTimestamp() - sentTicks) * 1000 / Stopwatch.Frequency, 0);
            LatencyText.Text = $"Ping: {rttMs} ms";
        }
    }

    private void UpdateScreenInfo(string payload)
    {
        try
        {
            _screenInfo = JsonSerializer.Deserialize<RemoteScreenInfo>(payload);
            if (_screenInfo is not null)
            {
                _activeDisplayIndex = _screenInfo.ActiveDisplayIndex;
                StatusText.Text = $"Uzak Çözünürlük: {_screenInfo.Width} x {_screenInfo.Height}";
                BuildDisplayButtons();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ekran bilgisi okunamadı: {ex.Message}";
        }
    }

    private void BuildDisplayButtons()
    {
        DisplayStackPanel.Children.Clear();
        if (_screenInfo?.Displays is null || _screenInfo.Displays.Length == 0)
        {
            var btn = CreateDisplayButton(0, "Tüm Ekranlar", _activeDisplayIndex == 0);
            DisplayStackPanel.Children.Add(btn);
            return;
        }

        foreach (var item in _screenInfo.Displays)
        {
            var btn = CreateDisplayButton(item.Index, item.Index == 0 ? "Tüm Ekranlar" : item.Index.ToString(), item.Index == _activeDisplayIndex);
            DisplayStackPanel.Children.Add(btn);
        }
    }

    private Button CreateDisplayButton(int displayIndex, string label, bool isActive)
    {
        var btn = new Button
        {
            Content = label,
            Tag = displayIndex,
            Style = (Style)FindResource("DisplayBtn"),
            Background = isActive ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB")) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F172A")),
            Foreground = isActive ? Brushes.White : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"))
        };

        btn.Click += async (s, e) =>
        {
            if (_sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
            {
                _activeDisplayIndex = displayIndex;
                BuildDisplayButtons();
                StatusText.Text = $"Ekran {displayIndex} geçişi yapılıyor...";
                try
                {
                    await _connection.InvokeAsync("SendSignal", _sessionId.Value, "select-display", displayIndex.ToString());
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"Ekran geçiş hatası: {ex.Message}";
                }
            }
        };

        return btn;
    }

    private void ShowFrame(string base64Jpeg)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64Jpeg);
            _bytesReceived += bytes.Length;

            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();

            RemoteImage.Source = image;
            if (PlaceholderPanel.Visibility == Visibility.Visible)
            {
                PlaceholderPanel.Visibility = Visibility.Collapsed;
            }

            CalculateMetrics();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Kare okunamadı: {ex.Message}";
        }
    }

    private void CalculateMetrics()
    {
        _frameCount++;
        var now = Stopwatch.GetTimestamp();
        var elapsedSeconds = (double)(now - _lastStatsTimestamp) / Stopwatch.Frequency;
        if (elapsedSeconds >= 1.0)
        {
            var fps = Math.Round(_frameCount / elapsedSeconds, 1);
            var kbps = Math.Round((_bytesReceived / 1024.0) / elapsedSeconds, 1);

            FpsText.Text = $"FPS: {fps} | {_screenInfo?.Width ?? 0}x{_screenInfo?.Height ?? 0}";
            ThroughputText.Text = $"Bant: {kbps} KB/s";

            _frameCount = 0;
            _bytesReceived = 0;
            _lastStatsTimestamp = now;
        }
    }

    private void StretchToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _scaleMode = (_scaleMode + 1) % 3;
        switch (_scaleMode)
        {
            case 0: // Sığdır
                RemoteImage.Stretch = Stretch.Uniform;
                RemoteImage.Width = double.NaN;
                RemoteImage.Height = double.NaN;
                StretchToggleBtn.Content = "📐 Ölçek: Sığdır";
                break;
            case 1: // 1:1 Birebir
                RemoteImage.Stretch = Stretch.None;
                RemoteImage.Width = _screenInfo?.Width ?? double.NaN;
                RemoteImage.Height = _screenInfo?.Height ?? double.NaN;
                StretchToggleBtn.Content = "🔍 Ölçek: 1:1 Birebir";
                break;
            case 2: // Esnet
                RemoteImage.Stretch = Stretch.Fill;
                RemoteImage.Width = double.NaN;
                RemoteImage.Height = double.NaN;
                StretchToggleBtn.Content = "↔️ Ölçek: Esnet";
                break;
        }
    }

    private async void QualityToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        _qualityMode = (_qualityMode + 1) % 3;
        QualityToggleBtn.Content = _qualityNames[_qualityMode];
        var targetQuality = _qualityValues[_qualityMode];

        if (_sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("SendSignal", _sessionId.Value, "set-quality", targetQuality.ToString());
            }
            catch { }
        }
    }

    private async void ClipboardBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text) && _sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
                {
                    await _connection.InvokeAsync("SendSignal", _sessionId.Value, "clipboard-text", text);
                    StatusText.Text = "Pano metni uzak cihaza kopyalandı.";
                }
            }
            else
            {
                StatusText.Text = "Yerel panoda metin bulunamadı.";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Pano aktarım hatası: {ex.Message}";
        }
    }

    private async void WinDBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected) return;
        try
        {
            // Win (0x5B) + D (0x44)
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x5B, IsDown: true));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x44, IsDown: true));
            await Task.Delay(80);
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x44, IsDown: false));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x5B, IsDown: false));
        }
        catch { }
    }

    private async void AltTabBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected) return;
        try
        {
            // Alt (0x12) + Tab (0x09)
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x12, IsDown: true));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x09, IsDown: true));
            await Task.Delay(80);
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x09, IsDown: false));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x12, IsDown: false));
        }
        catch { }
    }

    private async void CtrlAltDelBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected) return;
        try
        {
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x11, IsDown: true));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x12, IsDown: true));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x2E, IsDown: true));
            await Task.Delay(100);
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x2E, IsDown: false));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x12, IsDown: false));
            await SendRemoteInputAsync(new RemoteInputEvent(_sessionId.Value, "key", KeyCode: 0x11, IsDown: false));
            StatusText.Text = "Ctrl+Alt+Del sinyali gönderildi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Kısayol gönderilemedi: {ex.Message}";
        }
    }

    private async void ShowDevicesBtn_Click(object sender, RoutedEventArgs e)
    {
        _pingTimer?.Stop();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        SwitchToDeviceList();
        await LoadDevicesAsync();
    }

    private async void DisconnectBtn_Click(object sender, RoutedEventArgs e)
    {
        _pingTimer?.Stop();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
        SwitchToDeviceList();
        await LoadDevicesAsync();
    }

    private void RemoteSurface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!TryMapToRemote(e.GetPosition(RemoteSurface), out var x, out var y))
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var minimumTicks = Stopwatch.Frequency / 45;
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
            await Dispatcher.BeginInvoke(() => StatusText.Text = $"Input gönderilemedi: {ex.Message}");
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

        if (_scaleMode == 2) // Fill / Stretch
        {
            x = _screenInfo.Left + (int)Math.Round((surfacePoint.X / RemoteSurface.ActualWidth) * _screenInfo.Width);
            y = _screenInfo.Top + (int)Math.Round((surfacePoint.Y / RemoteSurface.ActualHeight) * _screenInfo.Height);
            return true;
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
        _pingTimer?.Stop();
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

public sealed class DeviceModel
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string ActiveUser { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public bool IsOnline { get; set; }

    public string StatusText => IsOnline ? "🟢 Online" : "🔴 Offline";
}
