using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using NexMote.Shared.Contracts;

namespace NexMote.TechnicianApp;

/// <summary>
/// Teknisyen masaüstü uygulamasının ana penceresi (MainWindow).
/// Görevleri:
/// 1. Cihaz listesini sunucudan çekip canlı olarak listeler.
/// 2. "nexmote://connect?..." deep-link parametrelerini ayrıştırıp hedef bilgisayara canlı masaüstü oturumu açar.
/// 3. Çoklu monitörleri soldan sağa fiziksel düzende yan yana veya tek tek tam boyutta gösterir.
/// 4. Fare ve klavye girdilerini hedef monitör koordinatlarına dönüştürüp SignalR ile iletir.
/// 5. Uzaktan CMD/PowerShell komut çalıştırma (UAC yükseltme seçeneğiyle) ve denetim sonuçlarını gösterir.
/// 6. Çift yönlü dosya aktarımı (File Transfer), güç yönetimi (Kapat/Yeniden Başlat/Kilitle) ve Ctrl+Alt+Del sinyali gönderir.
/// 7. 6 saniyelik Watchdog ile donma ve bağlantı kopmalarında otomatik yenileme tetikler.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly string RunningVersion =
        System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly HttpClient _http = new();
    private string _serverUrl = "https://nexmote.com";
    private string _loginEmail = "admin@nexmote.com";
    private string _loginPassword = "admin123";
    private string _selectedShell = "cmd";
    private string? _pendingCommandRequestId;
    private HubConnection? _connection;
    private Guid? _sessionId;
    private RemoteScreenInfo? _screenInfo;
    private long _lastMouseMoveTimestamp;

    // Multi-monitor view: display selection and stretch view modes
    private int _selectedDisplayIndex = 0; // 0 = Tüm Ekranlar, >0 = Seçili Ekran
    private Stretch _currentStretch = Stretch.Uniform;
    private readonly Dictionary<int, Image> _displayImages = new();
    private readonly Dictionary<int, DisplayItem> _displayMeta = new();
    private readonly Dictionary<int, Point> _lastRemotePointPerDisplay = new();

    // Performance & Stats metrics
    private int _frameCount;
    private long _bytesReceived;
    private long _lastStatsTimestamp = Stopwatch.GetTimestamp();
    private long _lastFrameReceivedTicks = Stopwatch.GetTimestamp();
    private bool _isFullScreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    // RTT Ping metrics
    private System.Windows.Threading.DispatcherTimer? _pingTimer;
    private long _pingSentTimestamp;

    public bool CredentialsReady { get; private set; } = true;
    private bool _isIslandPinned = true;
    private bool _isIslandCollapsed = false;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{Title} (v{RunningVersion})";

        RemoteScrollViewer.SizeChanged += (s, e) =>
        {
            if (_screenInfo != null && _displayImages.Count > 0)
            {
                BuildMultiScreenLayout();
            }
        };

        var launchedSession = ParseLaunchArguments();
        if (!launchedSession)
        {
            if (!EnsureServerCredentials())
            {
                CredentialsReady = false;
                return;
            }

            SwitchToDeviceList();
            _ = InitializeDeviceListAsync();
        }
        else
        {
            // Even when launched via deep-link, ensure background admin token is ready for later
            _ = EnsureAdminTokenAsync();
        }
    }

    private async Task InitializeDeviceListAsync()
    {
        if (!await EnsureAdminTokenAsync())
        {
            // Silent auto-login with stored/default credentials failed (e.g. admin password
            // was changed on the server). Fall back to prompting once instead of hammering
            // a protected endpoint with an invalid or missing token.
            if (!EnsureServerCredentials(forcePrompt: true) || !await EnsureAdminTokenAsync())
            {
                StatusText.Text = "Sunucuya giriş yapılamadı.";
                return;
            }
        }

        await LoadDevicesAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(2500);
            await Dispatcher.InvokeAsync(() => CheckForUpdatesAsync(isManual: false));
        });
    }

    private bool EnsureServerCredentials(bool forcePrompt = false)
    {
        var (storedUrl, storedEmail, storedPassword, storedToken) = TechnicianAppSettings.Load();

        if (string.IsNullOrWhiteSpace(storedUrl) || storedUrl.Contains("192.168") || storedUrl.Contains("127.0.0.1") || storedUrl.Contains("localhost") || storedUrl.StartsWith("http://"))
        {
            storedUrl = "https://nexmote.com";
            TechnicianAppSettings.Save(storedUrl, storedEmail ?? "admin@nexmote.com", storedPassword ?? "admin123", storedToken);
        }

        _serverUrl = storedUrl;
        _loginEmail = storedEmail ?? "admin@nexmote.com";
        _loginPassword = storedPassword ?? "admin123";

        if (!string.IsNullOrWhiteSpace(storedToken))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", storedToken);
        }

        // Requirement 1 & 2: Start directly without login prompt & use https://nexmote.com
        if (!forcePrompt)
        {
            return true;
        }

        var prompt = new ServerLoginWindow(_serverUrl, storedEmail ?? "admin@nexmote.com");
        if (prompt.ShowDialog() != true)
        {
            return false;
        }

        _serverUrl = prompt.ServerUrl;
        _loginEmail = prompt.Email;
        _loginPassword = prompt.Password;
        if (prompt.RememberMe)
        {
            TechnicianAppSettings.Save(_serverUrl, prompt.Email, prompt.Password);
        }
        return true;
    }

    private async Task<bool> EnsureAdminTokenAsync()
    {
        try
        {
            if (_http.DefaultRequestHeaders.Authorization != null)
            {
                return true;
            }

            var url = $"{_serverUrl.TrimEnd('/')}/api/auth/login";
            var response = await _http.PostAsJsonAsync(url, new AdminLoginRequest(_loginEmail, _loginPassword));
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var body = await response.Content.ReadFromJsonAsync<AdminLoginResponse>();
            if (body is null || string.IsNullOrWhiteSpace(body.Token))
            {
                return false;
            }

            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
            TechnicianAppSettings.Save(_serverUrl, _loginEmail, _loginPassword, body.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async void LogoutBtn_Click(object sender, RoutedEventArgs e)
    {
        TechnicianAppSettings.Clear();
        _http.DefaultRequestHeaders.Authorization = null;
        if (EnsureServerCredentials(forcePrompt: true) && await EnsureAdminTokenAsync())
        {
            SwitchToDeviceList();
            _ = LoadDevicesAsync();
        }
        else
        {
            Close();
        }
    }

    private void SwitchToDeviceList()
    {
        WindowState = WindowState.Normal;
        TopHeaderRow.Height = new GridLength(52);
        BottomStatusRow.Height = new GridLength(32);
        TopHeaderBorder.Visibility = Visibility.Visible;
        BottomStatusBorder.Visibility = Visibility.Visible;

        DeviceInventoryGrid.Visibility = Visibility.Visible;
        RemoteCanvasGrid.Visibility = Visibility.Collapsed;
        SessionText.Text = "Cihaz Seçimi";
        StatusText.Text = "Cihaz listesi yükleniyor...";

        MultiScreenPanel.Children.Clear();
        _displayImages.Clear();
        _displayMeta.Clear();
        _lastRemotePointPerDisplay.Clear();
        IslandDisplaysContextMenu.Items.Clear();
        _selectedDisplayIndex = 0;
        PlaceholderPanel.Visibility = Visibility.Visible;
    }

    private void SwitchToRemoteSession(string? deviceName = null)
    {
        // Requirement 3: Maximize window on remote session (Image 2)
        WindowState = WindowState.Maximized;

        TopHeaderRow.Height = new GridLength(0);
        BottomStatusRow.Height = new GridLength(0);
        TopHeaderBorder.Visibility = Visibility.Collapsed;
        BottomStatusBorder.Visibility = Visibility.Collapsed;

        SessionDeviceNameText.Text = string.IsNullOrWhiteSpace(deviceName) ? $"Oturum: {_sessionId.ToString()?[..8]}" : deviceName;

        DeviceInventoryGrid.Visibility = Visibility.Collapsed;
        RemoteCanvasGrid.Visibility = Visibility.Visible;
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
            query.TryGetValue("deviceName", out var deviceName);

            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                _serverUrl = serverUrl;
                TechnicianAppSettings.Save(_serverUrl, _loginEmail, _loginPassword);
            }

            SessionText.Text = $"Oturum: {sessionId}";
            if (!Guid.TryParse(sessionId, out var parsedSessionId) || string.IsNullOrWhiteSpace(token))
            {
                StatusText.Text = "Geçersiz oturum kimliği veya token.";
                return false;
            }

            _sessionId = parsedSessionId;
            SwitchToRemoteSession(deviceName);
            _ = ConnectSignalingAsync(parsedSessionId, token, _serverUrl);
            return true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Başlatma hatası: {ex.Message}";
            return false;
        }
    }

    private List<DeviceModel> _allDevices = new();

    private async Task LoadDevicesAsync()
    {
        try
        {
            if (_http.DefaultRequestHeaders.Authorization is null)
            {
                await EnsureAdminTokenAsync();
            }

            StatusText.Text = "Cihazlar getiriliyor...";
            var devices = await _http.GetFromJsonAsync<List<DeviceModel>>($"{_serverUrl.TrimEnd('/')}/api/devices");
            if (devices is not null)
            {
                _allDevices = devices;
                ApplyDeviceFilter();
                var onlineCount = devices.Count(d => d.IsOnline);
                TotalCountText.Text = devices.Count.ToString();
                OnlineCountText.Text = onlineCount.ToString();
                StatusText.Text = $"Toplam {devices.Count} cihaz bulundu ({onlineCount} online).";
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _http.DefaultRequestHeaders.Authorization = null;
            if (await EnsureAdminTokenAsync())
            {
                var devices = await _http.GetFromJsonAsync<List<DeviceModel>>($"{_serverUrl.TrimEnd('/')}/api/devices");
                if (devices is not null)
                {
                    _allDevices = devices;
                    ApplyDeviceFilter();
                    var onlineCount = devices.Count(d => d.IsOnline);
                    TotalCountText.Text = devices.Count.ToString();
                    OnlineCountText.Text = onlineCount.ToString();
                    StatusText.Text = $"Toplam {devices.Count} cihaz bulundu ({onlineCount} online).";
                    return;
                }
            }

            StatusText.Text = "Teknisyen erişim anahtarı geçersiz. Lütfen tekrar girin.";
            TechnicianAppSettings.Clear();
            if (EnsureServerCredentials(forcePrompt: true) && await EnsureAdminTokenAsync())
            {
                await LoadDevicesAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Cihaz listesi alınamadı: {ex.Message}";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyDeviceFilter();
    }

    private void ApplyDeviceFilter()
    {
        if (_allDevices == null) return;
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            DevicesDataGrid.ItemsSource = _allDevices;
            return;
        }

        DevicesDataGrid.ItemsSource = _allDevices.Where(d =>
            (d.DeviceName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.IpAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.ActiveUser?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.LocationCode?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.OperatingSystem?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
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

                if (string.Equals(type, "screen-frame-multi", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => ShowFrame(payload));
                    return;
                }

                if (string.Equals(type, "command-result", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => ShowCommandResult(payload));
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

                // Watchdog: If no frame received in over 6 seconds during an active session, request screen refresh
                var secondsSinceLastFrame = (double)(Stopwatch.GetTimestamp() - _lastFrameReceivedTicks) / Stopwatch.Frequency;
                if (secondsSinceLastFrame > 6.0)
                {
                    StatusText.Text = "🟡 Görüntü yenileniyor (Otomatik Kurtarma)...";
                    try
                    {
                        await _connection.InvokeAsync("SendSignal", _sessionId.Value, "refresh-screen", "");
                    }
                    catch { }
                }
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
                var displays = (_screenInfo.Displays ?? Array.Empty<DisplayItem>())
                    .Where(d => d.Index > 0)
                    .OrderBy(d => d.Left)
                    .ThenBy(d => d.Index)
                    .ToList();

                var count = displays.Count;
                StatusText.Text = count > 1
                    ? $"{count} ekran eş zamanlı akıyor (Soldan sağa sıralı)."
                    : $"Uzak Çözünürlük: {_screenInfo.Width} x {_screenInfo.Height}";

                UpdateDisplaySelectorUI(displays);
                BuildMultiScreenLayout();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ekran bilgisi okunamadı: {ex.Message}";
        }
    }

    private void UpdateDisplaySelectorUI(List<DisplayItem> displays)
    {
        IslandDisplaysContextMenu.Items.Clear();

        var allItem = new MenuItem
        {
            Header = $"🖥️ Tüm Ekranlar ({displays.Count})",
            Tag = 0
        };
        allItem.Click += (s, e) => SelectDisplay(0, "Tüm Ekranlar");
        IslandDisplaysContextMenu.Items.Add(allItem);

        if (displays.Count > 1)
        {
            IslandDisplaysContextMenu.Items.Add(new Separator());
        }

        for (int i = 0; i < displays.Count; i++)
        {
            var d = displays[i];
            var item = new MenuItem
            {
                Header = $"🖥️ {d.Name} ({d.Width}x{d.Height})",
                Tag = d.Index
            };
            item.Click += (s, e) => SelectDisplay(d.Index, d.Name);
            IslandDisplaysContextMenu.Items.Add(item);
        }
    }

    private void SelectDisplay(int index, string displayName)
    {
        _selectedDisplayIndex = index;
        BuildMultiScreenLayout();
        StatusText.Text = _selectedDisplayIndex == 0
            ? "Tüm ekranlar eş zamanlı gösteriliyor."
            : $"{displayName} tam boyuta alındı.";
    }

    private void PinIslandBtn_Click(object sender, RoutedEventArgs e)
    {
        _isIslandPinned = !_isIslandPinned;
        PinIslandBtn.Content = _isIslandPinned ? "📌" : "📍";
        PinIslandBtn.ToolTip = _isIslandPinned ? "Araç Çubuğunu Sabitle / Otomatik Gizle" : "Otomatik Gizle Açık";
    }

    private void IslandCollapseHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isIslandCollapsed = !_isIslandCollapsed;
        FloatingIslandPill.Visibility = _isIslandCollapsed ? Visibility.Collapsed : Visibility.Visible;
        IslandCollapseIcon.Text = _isIslandCollapsed ? "::: ⌄" : "::: ⌃";
    }

    private void IslandDisplaysBtn_Click(object sender, RoutedEventArgs e)
    {
        if (IslandDisplaysContextMenu != null)
        {
            IslandDisplaysContextMenu.PlacementTarget = IslandDisplaysBtn;
            IslandDisplaysContextMenu.IsOpen = true;
        }
    }

    private void ViewModeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void PowerMenuBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void ViewModeFit_Click(object sender, RoutedEventArgs e)
    {
        _currentStretch = Stretch.Uniform;
        BuildMultiScreenLayout();
        StatusText.Text = "Görünüm: Ekrana Sığdır (Orantılı)";
    }

    private void ViewModeStretch_Click(object sender, RoutedEventArgs e)
    {
        _currentStretch = Stretch.Fill;
        BuildMultiScreenLayout();
        StatusText.Text = "Görünüm: Ekrana Yay (Tam Doldur)";
    }

    private void ViewModeOriginal_Click(object sender, RoutedEventArgs e)
    {
        _currentStretch = Stretch.None;
        BuildMultiScreenLayout();
        StatusText.Text = "Görünüm: Orijinal Boyut (1:1 Piksel)";
    }

    private void BuildMultiScreenLayout()
    {
        MultiScreenPanel.Children.Clear();
        _displayImages.Clear();
        _displayMeta.Clear();
        _lastRemotePointPerDisplay.Clear();

        var allDisplays = (_screenInfo?.Displays ?? Array.Empty<DisplayItem>())
            .Where(d => d.Index > 0)
            .OrderBy(d => d.Left)
            .ThenBy(d => d.Index)
            .ToList();

        if (allDisplays.Count == 0) return;

        foreach (var d in allDisplays)
        {
            _displayMeta[d.Index] = d;
        }

        var visibleDisplays = _selectedDisplayIndex == 0
            ? allDisplays
            : allDisplays.Where(d => d.Index == _selectedDisplayIndex).ToList();

        if (visibleDisplays.Count == 0)
        {
            visibleDisplays = allDisplays;
            _selectedDisplayIndex = 0;
        }

        var isSingleView = visibleDisplays.Count <= 1;
        var availWidth = RemoteScrollViewer.ActualWidth > 50 ? RemoteScrollViewer.ActualWidth : 1200;
        var availHeight = RemoteScrollViewer.ActualHeight > 50 ? RemoteScrollViewer.ActualHeight : 700;

        foreach (var display in visibleDisplays)
        {
            var displayIndex = display.Index;
            double tileWidth;
            double tileHeight;

            if (_currentStretch == Stretch.None)
            {
                tileWidth = display.Width;
                tileHeight = display.Height;
            }
            else if (isSingleView) // Single display full viewport mode
            {
                tileWidth = availWidth;
                tileHeight = availHeight;
            }
            else // Multi display side-by-side mode
            {
                var aspect = display.Height > 0 ? display.Width / (double)display.Height : 16.0 / 9.0;
                var totalScreens = visibleDisplays.Count;
                var maxPerScreenWidth = Math.Max(320, (availWidth - (totalScreens * 6)) / totalScreens);
                tileHeight = Math.Min(availHeight, maxPerScreenWidth / aspect);
                tileWidth = tileHeight * aspect;
            }

            var image = new Image
            {
                Stretch = _currentStretch,
                Width = tileWidth,
                Height = tileHeight,
                Focusable = true,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

            image.MouseMove += (s, e) => HandleTileMouseMove(displayIndex, image, e);
            image.MouseLeftButtonDown += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "left", true); e.Handled = true; };
            image.MouseLeftButtonUp += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "left", false); e.Handled = true; };
            image.MouseRightButtonDown += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "right", true); e.Handled = true; };
            image.MouseRightButtonUp += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "right", false); e.Handled = true; };
            image.MouseDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    HandleTileMouseButton(displayIndex, image, e, "middle", true);
                    e.Handled = true;
                }
            };
            image.MouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle)
                {
                    HandleTileMouseButton(displayIndex, image, e, "middle", false);
                    e.Handled = true;
                }
            };
            image.MouseWheel += (s, e) =>
            {
                RemoteScrollViewer.Focus();
                _ = SendRemoteInputAsync(new RemoteInputEvent(_sessionId ?? Guid.Empty, "mouse-wheel", WheelDelta: e.Delta, DisplayIndex: displayIndex));
            };

            var label = new TextBlock
            {
                Text = display.Name,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(170, 15, 23, 42)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(6),
                Visibility = isSingleView ? Visibility.Collapsed : Visibility.Visible
            };

            var tileGrid = new Grid { Width = tileWidth, Height = tileHeight };
            tileGrid.Children.Add(image);
            tileGrid.Children.Add(label);

            var tileBorder = new Border
            {
                Child = tileGrid,
                CornerRadius = isSingleView ? new CornerRadius(0) : new CornerRadius(6),
                ClipToBounds = true,
                BorderBrush = isSingleView ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
                BorderThickness = isSingleView ? new Thickness(0) : new Thickness(1),
                Margin = isSingleView ? new Thickness(0) : new Thickness(2),
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00))
            };

            _displayImages[display.Index] = image;
            MultiScreenPanel.Children.Add(tileBorder);
        }
    }

    private bool TryMapTileToRemote(int displayIndex, Image image, Point tilePoint, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!_displayMeta.TryGetValue(displayIndex, out var meta) || image.ActualWidth <= 0 || image.ActualHeight <= 0 || meta.Width <= 0 || meta.Height <= 0)
        {
            return false;
        }

        if (image.Stretch == Stretch.Fill)
        {
            var scaleX = image.ActualWidth / meta.Width;
            var scaleY = image.ActualHeight / meta.Height;
            x = (int)Math.Round(tilePoint.X / scaleX);
            y = (int)Math.Round(tilePoint.Y / scaleY);
            x = Math.Clamp(x, 0, meta.Width - 1);
            y = Math.Clamp(y, 0, meta.Height - 1);
            return true;
        }
        else if (image.Stretch == Stretch.None)
        {
            var offsetX = (image.ActualWidth - meta.Width) / 2.0;
            var offsetY = (image.ActualHeight - meta.Height) / 2.0;
            var imageX = tilePoint.X - offsetX;
            var imageY = tilePoint.Y - offsetY;
            if (imageX < 0 || imageY < 0 || imageX > meta.Width || imageY > meta.Height)
            {
                return false;
            }
            x = (int)Math.Round(imageX);
            y = (int)Math.Round(imageY);
            x = Math.Clamp(x, 0, meta.Width - 1);
            y = Math.Clamp(y, 0, meta.Height - 1);
            return true;
        }
        else // Stretch.Uniform
        {
            var scale = Math.Min(image.ActualWidth / meta.Width, image.ActualHeight / meta.Height);
            var renderedWidth = meta.Width * scale;
            var renderedHeight = meta.Height * scale;
            var offsetX = (image.ActualWidth - renderedWidth) / 2.0;
            var offsetY = (image.ActualHeight - renderedHeight) / 2.0;
            var imageX = tilePoint.X - offsetX;
            var imageY = tilePoint.Y - offsetY;

            if (imageX < 0 || imageY < 0 || imageX > renderedWidth || imageY > renderedHeight)
            {
                return false;
            }

            x = (int)Math.Round(imageX / scale);
            y = (int)Math.Round(imageY / scale);
            x = Math.Clamp(x, 0, meta.Width - 1);
            y = Math.Clamp(y, 0, meta.Height - 1);
            return true;
        }
    }

    private void HandleTileMouseMove(int displayIndex, Image image, MouseEventArgs e)
    {
        if (!TryMapTileToRemote(displayIndex, image, e.GetPosition(image), out var x, out var y))
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var minimumTicks = Stopwatch.Frequency / 45;
        var lastPoint = _lastRemotePointPerDisplay.TryGetValue(displayIndex, out var lp) ? lp : new Point(-999, -999);
        var moved = Math.Abs(x - lastPoint.X) >= 2 || Math.Abs(y - lastPoint.Y) >= 2;
        if (_lastMouseMoveTimestamp != 0 && now - _lastMouseMoveTimestamp < minimumTicks && !moved)
        {
            return;
        }

        _lastMouseMoveTimestamp = now;
        _lastRemotePointPerDisplay[displayIndex] = new Point(x, y);
        _ = SendRemoteInputAsync(new RemoteInputEvent(_sessionId ?? Guid.Empty, "mouse-move", x, y, DisplayIndex: displayIndex));
    }

    private void HandleTileMouseButton(int displayIndex, Image image, MouseButtonEventArgs e, string button, bool isDown)
    {
        if (!TryMapTileToRemote(displayIndex, image, e.GetPosition(image), out var x, out var y))
        {
            if (isDown || !image.IsMouseCaptured || !_lastRemotePointPerDisplay.TryGetValue(displayIndex, out var lastKnown))
            {
                return;
            }

            x = (int)Math.Round(lastKnown.X);
            y = (int)Math.Round(lastKnown.Y);
        }

        RemoteScrollViewer.Focus();
        _lastRemotePointPerDisplay[displayIndex] = new Point(x, y);
        if (isDown)
        {
            image.CaptureMouse();
        }
        else if (image.IsMouseCaptured)
        {
            image.ReleaseMouseCapture();
        }

        _ = SendRemoteInputAsync(new RemoteInputEvent(_sessionId ?? Guid.Empty, "mouse-button", x, y, button, isDown, DisplayIndex: displayIndex));
    }

    private void ShowFrame(string payload)
    {
        try
        {
            var frame = JsonSerializer.Deserialize<MultiScreenFrame>(payload);
            if (frame is null || !_displayImages.TryGetValue(frame.DisplayIndex, out var image))
            {
                return;
            }

            _lastFrameReceivedTicks = Stopwatch.GetTimestamp();
            var bytes = Convert.FromBase64String(frame.JpegBase64);
            _bytesReceived += bytes.Length;

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            image.Source = bitmap;
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

    private async void RefreshScreenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
        {
            StatusText.Text = "🔄 Ekran akışı ve bilgisi yenileniyor...";
            try
            {
                await _connection.InvokeAsync("SendSignal", _sessionId.Value, "refresh-screen", "");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Yenileme hatası: {ex.Message}";
            }
        }
    }

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _previousWindowStyle = WindowStyle;
            _previousWindowState = WindowState;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            _isFullScreen = true;
        }
        else
        {
            WindowStyle = _previousWindowStyle;
            WindowState = _previousWindowState;
            _isFullScreen = false;
        }
    }

    private async void CheckUpdatesBtn_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(isManual: true);
    }

    private async Task CheckForUpdatesAsync(bool isManual)
    {
        try
        {
            if (isManual)
            {
                CheckUpdatesBtn.IsEnabled = false;
                CheckUpdatesBtn.Content = "⏳ Kontrol Ediliyor...";
            }

            using var http = new HttpClient();
            var checkUrl = $"{_serverUrl.TrimEnd('/')}/api/updates/check";
            var json = await http.GetStringAsync(checkUrl);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("technician", out var tech) &&
                tech.TryGetProperty("version", out var verProp) &&
                tech.TryGetProperty("downloadUrl", out var urlProp))
            {
                var latestVersion = verProp.GetString();
                var downloadUrl = urlProp.GetString();
                var releaseNotes = tech.TryGetProperty("releaseNotes", out var notesProp) ? notesProp.GetString() : "Performans ve kararlılık iyileştirmesi.";

                var isNewer = Version.TryParse(latestVersion, out var latest) &&
                              Version.TryParse(RunningVersion, out var current) &&
                              latest > current;

                if (!isNewer)
                {
                    if (isManual)
                    {
                        MessageBox.Show(
                            $"Sisteminiz zaten en güncel sürümde (v{RunningVersion}).",
                            "Güncelleme Kontrolü",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    return;
                }

                var result = MessageBox.Show(
                    $"NexMote Teknisyen Konsolu için yeni bir güncelleme mevcut!\n\n" +
                    $"Mevcut Sürüm: v{RunningVersion}\n" +
                    $"Yeni Sürüm: v{latestVersion}\n" +
                    $"Açıklama: {releaseNotes}\n\n" +
                    $"Güncellemeyi şimdi indirmek ve yüklemek istiyor musunuz?",
                    "NexMote Teknisyen Güncellemesi",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(downloadUrl))
                {
                    StatusText.Text = "🚀 Güncelleme MSI paketi indiriliyor...";
                    var tempMsi = Path.Combine(Path.GetTempPath(), "NexMote-Technician-Update.msi");
                    var msiBytes = await http.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(tempMsi, msiBytes);

                    StatusText.Text = "⚙️ Yükleyici çalıştırılıyor (yönetici izni gerekebilir)...";
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("msiexec.exe", $"/i \"{tempMsi}\" /qn /norestart")
                        {
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        System.Diagnostics.Process.Start(psi);
                        MessageBox.Show("Güncelleme başlatıldı! Açılan yönetici izni istemini onaylayın; kurulum arka planda birkaç saniye içinde tamamlanacak.", "Güncelleme", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
                    {
                        StatusText.Text = "Güncelleme iptal edildi (yönetici izni verilmedi).";
                    }
                }
            }
            else if (isManual)
            {
                MessageBox.Show("Sisteminiz zaten en güncel sürümde.", "Güncelleme Kontrolü", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            if (isManual)
            {
                MessageBox.Show($"Güncelleme kontrolü yapılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            if (isManual)
            {
                CheckUpdatesBtn.IsEnabled = true;
                CheckUpdatesBtn.Content = "🚀 Güncelleme Kontrol Et";
            }
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

            FpsText.Text = $"FPS: {fps} | {_displayImages.Count} Ekran";
            ThroughputText.Text = $"Bant: {kbps} KB/s";
            if (SessionStatsText != null)
            {
                SessionStatsText.Text = $"{fps} FPS · {kbps} KB/s";
            }

            _frameCount = 0;
            _bytesReceived = 0;
            _lastStatsTimestamp = now;
        }
    }

    private async Task SendPowerActionAsync(string action, string label)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected) return;
        var confirm = MessageBox.Show($"Uzak bilgisayara '{label}' komutu gönderilecek. Devam etmek istiyor musunuz?", "NexMote Güç Yönetimi", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var req = new PowerActionRequest(_sessionId.Value, action);
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "power-action", JsonSerializer.Serialize(req));
            StatusText.Text = $"⚡ {label} komutu iletildi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Güç işlemi başarısız: {ex.Message}";
        }
    }

    private async void PowerLock_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("lock", "Kilitle");
    private async void PowerLogoff_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("logoff", "Oturumu Kapat");
    private async void PowerReboot_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("reboot", "Yeniden Başlat");
    private async void PowerRebootSafe_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("reboot-safe", "Güvenli Modda Yeniden Başlat");
    private async void PowerRebootNormal_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("reboot-normal", "Normal Modda Yeniden Başlat");
    private async void PowerShutdown_Click(object sender, RoutedEventArgs e) => await SendPowerActionAsync("shutdown", "Kapat");

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

    private readonly HashSet<int> _downKeys = new();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (RemoteCanvasGrid.Visibility != Visibility.Visible) return;
        if (CommandPanel.Visibility == Visibility.Visible && CommandInputBox.IsFocused) return;

        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // Fiziksel tuş basılı tutulduğunda işletim sisteminin ürettiği mükerrer KeyDown sinyallerini engelle.
        // Uzak bilgisayar sürücüsü tuş basılı olduğu sürece kendi donanımsal tekrarını otomatik yönetir.
        if (e.IsRepeat)
        {
            e.Handled = true;
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var keyCode = KeyInterop.VirtualKeyFromKey(key);
        if (keyCode == 0)
        {
            return;
        }

        lock (_downKeys)
        {
            if (!_downKeys.Add(keyCode))
            {
                e.Handled = true;
                return;
            }
        }

        SendKey(keyCode, true);
        e.Handled = true;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (RemoteCanvasGrid.Visibility != Visibility.Visible) return;
        if (CommandPanel.Visibility == Visibility.Visible && CommandInputBox.IsFocused) return;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var keyCode = KeyInterop.VirtualKeyFromKey(key);
        if (keyCode == 0)
        {
            return;
        }

        lock (_downKeys)
        {
            _downKeys.Remove(keyCode);
        }

        SendKey(keyCode, false);
        e.Handled = true;
    }

    private void SendKey(int keyCode, bool isDown)
    {
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

    protected override async void OnClosed(EventArgs e)
    {
        _pingTimer?.Stop();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        base.OnClosed(e);
    }

    private async void SendFileBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            StatusText.Text = "Dosya göndermek için aktif bir oturum gerekir.";
            return;
        }

        var dialog = new OpenFileDialog { Title = "Uzak cihaza gönderilecek dosyayı seçin" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await SendFileAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Dosya gönderilemedi: {ex.Message}";
        }
    }

    private async Task SendFileAsync(string filePath)
    {
        const int ChunkSize = 200 * 1024;
        var fileInfo = new FileInfo(filePath);
        var totalChunks = Math.Max(1, (int)Math.Ceiling(fileInfo.Length / (double)ChunkSize));
        var transferId = Guid.NewGuid();
        var sessionId = _sessionId!.Value;

        await using var stream = File.OpenRead(filePath);
        var buffer = new byte[ChunkSize];

        for (var index = 0; index < totalChunks; index++)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, ChunkSize));
            var chunk = new FileTransferChunk(
                sessionId,
                transferId,
                fileInfo.Name,
                fileInfo.Length,
                index,
                totalChunks,
                Convert.ToBase64String(buffer, 0, read),
                index == totalChunks - 1);

            await _connection!.InvokeAsync("SendSignal", sessionId, "file-chunk", JsonSerializer.Serialize(chunk));
            StatusText.Text = $"Dosya gönderiliyor: {fileInfo.Name} ({index + 1}/{totalChunks})";
        }

        StatusText.Text = $"Dosya gönderildi: {fileInfo.Name}";
    }

    private void CommandPanelToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        CommandPanel.Visibility = CommandPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (CommandPanel.Visibility == Visibility.Visible)
        {
            SetSelectedShell(_selectedShell);
            CommandInputBox.Focus();
        }
    }

    private void ShellCmdBtn_Click(object sender, RoutedEventArgs e) => SetSelectedShell("cmd");

    private void ShellPsBtn_Click(object sender, RoutedEventArgs e) => SetSelectedShell("powershell");

    private void SetSelectedShell(string shell)
    {
        _selectedShell = shell;
        var activeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
        var inactiveBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F5F9"));
        var inactiveForeground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"));

        ShellCmdBtn.Background = shell == "cmd" ? activeBrush : inactiveBrush;
        ShellCmdBtn.Foreground = shell == "cmd" ? Brushes.White : inactiveForeground;
        ShellPsBtn.Background = shell == "powershell" ? activeBrush : inactiveBrush;
        ShellPsBtn.Foreground = shell == "powershell" ? Brushes.White : inactiveForeground;
    }

    private void CommandInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunCommandBtn_Click(sender, e);
        }
    }

    private async void RunCommandBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        var command = CommandInputBox.Text.Trim();
        if (string.IsNullOrEmpty(command))
        {
            return;
        }

        var runAsAdmin = RunAsAdminCheckBox.IsChecked == true;
        _pendingCommandRequestId = Guid.NewGuid().ToString("N");
        CommandOutputBox.Text += $"> {(runAsAdmin ? "🛡️ [Yönetici] " : string.Empty)}{command}{Environment.NewLine}";
        CommandOutputBox.ScrollToEnd();
        CommandInputBox.Clear();

        var request = new RemoteCommandRequest(_sessionId.Value, _pendingCommandRequestId, _selectedShell, command, runAsAdmin);
        try
        {
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "remote-command", JsonSerializer.Serialize(request));
        }
        catch (Exception ex)
        {
            CommandOutputBox.Text += $"[Gönderim hatası: {ex.Message}]{Environment.NewLine}";
            CommandOutputBox.ScrollToEnd();
        }
    }

    private void ShowCommandResult(string payload)
    {
        try
        {
            var result = JsonSerializer.Deserialize<RemoteCommandResult>(payload);
            if (result is null || result.RequestId != _pendingCommandRequestId)
            {
                return;
            }

            if (!string.IsNullOrEmpty(result.StdOut))
            {
                CommandOutputBox.Text += result.StdOut + Environment.NewLine;
            }

            if (!string.IsNullOrEmpty(result.StdErr))
            {
                CommandOutputBox.Text += result.StdErr + Environment.NewLine;
            }

            CommandOutputBox.Text += result.ElevationDenied
                ? $"[⚠️ Yönetici izni reddedildi veya UAC istemi zaman aşımına uğradı]{Environment.NewLine}{Environment.NewLine}"
                : result.TimedOut
                    ? $"[Zaman aşımı]{Environment.NewLine}{Environment.NewLine}"
                    : $"[Çıkış kodu: {result.ExitCode}] ({result.DurationMs} ms){Environment.NewLine}{Environment.NewLine}";

            CommandOutputBox.ScrollToEnd();
        }
        catch (Exception ex)
        {
            CommandOutputBox.Text += $"[Sonuç okunamadı: {ex.Message}]{Environment.NewLine}";
            CommandOutputBox.ScrollToEnd();
        }
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

/// <summary>
/// Teknisyen uygulamasındaki cihaz listesi WPF ListBox bileşenine bağlanan veri modeli.
/// </summary>
public sealed class DeviceModel
{
    public Guid Id { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = string.Empty;
    public string ActiveUser { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string LocationCode { get; set; } = string.Empty;
    public string AgentVersion { get; set; } = string.Empty;
    public double CpuUsagePercent { get; set; }
    public long MemoryTotalMb { get; set; }
    public long MemoryUsedMb { get; set; }
    public bool IsOnline { get; set; }

    public string StatusLabel => IsOnline ? "Çevrimiçi" : "Çevrimdışı";
    public string StatusBadgeBg => IsOnline ? "#ECFDF5" : "#F1F5F9";
    public string StatusBadgeFg => IsOnline ? "#047857" : "#64748B";
    public Brush StatusDotBrush => IsOnline
        ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
        : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    public string StatusText => IsOnline ? "🟢 Online" : "🔴 Offline";
}

/// <summary>
/// Teknisyen uygulamasının sunucu URL'ini ve e-posta ayarlarını (%AppData%\NexMote\TechnicianApp\settings.json) saklayan statik sınıf.
/// </summary>
internal static class TechnicianAppSettings
{
    private static string SettingsPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NexMote", "TechnicianApp", "settings.json");

    public static (string? ServerUrl, string? Email, string? Password, string? Token) Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return ("https://nexmote.com", "admin@nexmote.com", "admin123", null);
            }

            var data = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(SettingsPath));
            var url = data?.ServerUrl;
            if (string.IsNullOrWhiteSpace(url) || url.Contains("192.168.0") || url.Contains("127.0.0.1") || url.Contains("localhost") || url.StartsWith("http://"))
            {
                url = "https://nexmote.com";
                Save(url, data?.Email ?? "admin@nexmote.com", data?.Password ?? "admin123", data?.Token);
            }

            return (url, data?.Email ?? "admin@nexmote.com", data?.Password ?? "admin123", data?.Token);
        }
        catch
        {
            return ("https://nexmote.com", "admin@nexmote.com", "admin123", null);
        }
    }

    public static void Save(string serverUrl, string email = "admin@nexmote.com", string password = "admin123", string? token = null)
    {
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(new StoredSettings(serverUrl, email, password, token)));
        }
        catch
        {
            // Best-effort persistence; the in-memory value is still usable for this run.
        }
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                File.Delete(SettingsPath);
            }
        }
        catch
        {
        }
    }

    private sealed record StoredSettings(string ServerUrl, string Email, string? Password = "admin123", string? Token = null);
}
