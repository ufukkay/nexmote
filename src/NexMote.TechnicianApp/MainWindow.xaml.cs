using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Win32;
using NexMote.Shared.Contracts;
using NexMote.Shared.Network;

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

    private readonly HttpClient _http = NexMoteHttp.CreateClient();
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
    private int _remoteInputSentCount;

    // Performance & Stats metrics
    private int _frameCount;
    private long _bytesReceived;
    private long _lastStatsTimestamp = Stopwatch.GetTimestamp();
    private long _lastFrameReceivedTicks = Stopwatch.GetTimestamp();
    private long _remoteInputSequence;
    private double _smoothedLatencyMs;
    private readonly Queue<double> _latencySamples = new();
    private readonly Dictionary<int, int> _framesPerDisplay = new();
    private bool _isFullScreen;
    private WindowStyle _previousWindowStyle;
    private WindowState _previousWindowState;

    // RTT Ping metrics
    private System.Windows.Threading.DispatcherTimer? _pingTimer;
    private long _pingSentTimestamp;

    public bool CredentialsReady { get; private set; } = true;
    private bool _isIslandCollapsed = false;
    private Guid _currentConnectedDeviceId = Guid.Empty;
    private bool _isAwaitingReboot = false;
    private CancellationTokenSource? _rebootWatchdogCts;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"{Title} (v{RunningVersion})";
        UpdateHeaderIdentity();

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

        _serverUrl = NexMoteHttp.NormalizeUrl(storedUrl);
        _loginEmail = storedEmail ?? "admin@nexmote.com";
        _loginPassword = storedPassword ?? "admin123";
        UpdateHeaderIdentity();

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
        UpdateHeaderIdentity();
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
        UpdateHeaderIdentity();

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
            if (query.TryGetValue("deviceId", out var devIdStr) && Guid.TryParse(devIdStr, out var parsedDeviceId))
            {
                _currentConnectedDeviceId = parsedDeviceId;
            }

            if (!string.IsNullOrWhiteSpace(serverUrl))
            {
                _serverUrl = serverUrl;
                TechnicianAppSettings.Save(_serverUrl, _loginEmail, _loginPassword);
                UpdateHeaderIdentity();
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
                var offlineCount = devices.Count - onlineCount;
                TotalCountText.Text = devices.Count.ToString();
                OnlineCountText.Text = onlineCount.ToString();
                OfflineCountText.Text = offlineCount.ToString();
                StatusText.Text = $"Toplam {devices.Count} cihaz bulundu ({onlineCount} çevrimiçi).";
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
                    var offlineCount = devices.Count - onlineCount;
                    TotalCountText.Text = devices.Count.ToString();
                    OnlineCountText.Text = onlineCount.ToString();
                    OfflineCountText.Text = offlineCount.ToString();
                    StatusText.Text = $"Toplam {devices.Count} cihaz bulundu ({onlineCount} çevrimiçi).";
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
            FilterSummaryText.Text = $"Tüm cihazlar gösteriliyor · {_allDevices.Count} kayıt";
            return;
        }

        var filtered = _allDevices.Where(d =>
            (d.DeviceName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.IpAddress?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.ActiveUser?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.LocationCode?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.OperatingSystem?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();

        DevicesDataGrid.ItemsSource = filtered;
        FilterSummaryText.Text = $"{filtered.Count} kayıt eşleşti · arama: {query}";
    }

    private void UpdateHeaderIdentity()
    {
        if (HeaderServerUrlText is not null)
        {
            HeaderServerUrlText.Text = _serverUrl;
        }

        if (HeaderUserText is not null)
        {
            HeaderUserText.Text = string.IsNullOrWhiteSpace(_loginEmail) ? "Yönetici" : _loginEmail;
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
            _currentConnectedDeviceId = deviceId;
            StatusText.Text = "Uzaktan oturum başlatılıyor...";
            var request = new CreateRemoteSessionRequest(deviceId);
            var response = await _http.PostAsJsonAsync($"{_serverUrl.TrimEnd('/')}/api/remote-sessions", request);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                StatusText.Text = $"Oturum başlatılamadı: {err}";
                MessageBox.Show($"Cihaza bağlanılamadı. Cihazın çevrimiçi olduğundan emin olun.\n({err})", "Bağlantı hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => NexMoteHttp.CreateHandler();
                })
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
                    _ = ProcessIncomingFrameAsync(payload);
                    return;
                }

                if (string.Equals(type, "network-probe-ack", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => HandleNetworkProbeAck(payload));
                    return;
                }

                if (string.Equals(type, "input-ack", StringComparison.OrdinalIgnoreCase))
                {
                    Dispatcher.Invoke(() => StatusText.Text = "Uzak girdi hedefte uygulandı.");
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
                if (_isAwaitingReboot && _currentConnectedDeviceId != Guid.Empty)
                {
                    Dispatcher.Invoke(() =>
                    {
                        PlaceholderPanel.Visibility = Visibility.Visible;
                        PlaceholderTitle.Text = "Uzak Bilgisayar Yeniden Başlatılıyor";
                        PlaceholderText.Text = "Cihaz ile bağlantı kesildi. Windows açıldığında oturum otomatik olarak bağlanacaktır.";
                        StatusText.Text = "Uzak cihazın açılması bekleniyor (Otomatik Yeniden Bağlanma)...";
                    });

                    _ = StartRebootRecoveryWatchdogAsync(_currentConnectedDeviceId);
                    return Task.CompletedTask;
                }

                if (_currentConnectedDeviceId != Guid.Empty && RemoteCanvasGrid.Visibility == Visibility.Visible)
                {
                    Dispatcher.Invoke(() =>
                    {
                        PlaceholderPanel.Visibility = Visibility.Visible;
                        PlaceholderTitle.Text = "Bağlantı Kesildi";
                        PlaceholderText.Text = "Uzak cihaz ile bağlantı koptu. 'Şimdi Yeniden Bağlan' butonuna basabilir veya bekleyebilirsiniz.";
                        StatusText.Text = "Bağlantı kapandı. Yeniden bağlanılıyor...";
                    });
                }
                else
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Bağlantı kapandı: {error?.Message ?? "oturum sonlandırıldı."}");
                }
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
                    var probe = new NetworkProbe(Guid.NewGuid(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    await _connection.InvokeAsync("SendSignal", _sessionId.Value, "network-probe", JsonSerializer.Serialize(probe));
                }
                catch { }

                // Watchdog: If no frame received in over 12 seconds during an active session, request screen refresh
                var secondsSinceLastFrame = (double)(Stopwatch.GetTimestamp() - _lastFrameReceivedTicks) / Stopwatch.Frequency;
                if (secondsSinceLastFrame > 12.0)
                {
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
            AddLatencySample(rttMs);
        }
    }

    private void HandleNetworkProbeAck(string payload)
    {
        try
        {
            var ack = JsonSerializer.Deserialize<NetworkProbeAck>(payload);
            if (ack is null)
            {
                return;
            }

            var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            AddLatencySample(Math.Max(0, nowMs - ack.SentAtUnixMs));
        }
        catch
        {
        }
    }

    private void AddLatencySample(double rttMs)
    {
        _latencySamples.Enqueue(rttMs);
        while (_latencySamples.Count > 24)
        {
            _latencySamples.Dequeue();
        }

        _smoothedLatencyMs = _smoothedLatencyMs <= 0 ? rttMs : (_smoothedLatencyMs * 0.75) + (rttMs * 0.25);
        var p95 = _latencySamples.OrderBy(x => x).Skip(Math.Max(0, (int)Math.Ceiling(_latencySamples.Count * 0.95) - 1)).FirstOrDefault();
        LatencyText.Text = $"RTT: {Math.Round(_smoothedLatencyMs)} ms · p95 {Math.Round(p95)} ms";
    }

    private string _lastScreenInfoFingerprint = string.Empty;

    private void UpdateScreenInfo(string payload)
    {
        try
        {
            var screenInfo = JsonSerializer.Deserialize<RemoteScreenInfo>(payload);
            if (screenInfo is not null)
            {
                var displays = (screenInfo.Displays ?? Array.Empty<DisplayItem>())
                    .Where(d => d.Index > 0)
                    .OrderBy(d => d.Left)
                    .ThenBy(d => d.Index)
                    .ToList();

                var fingerprint = $"{screenInfo.Width}x{screenInfo.Height}_{displays.Count}_" +
                                  string.Join(",", displays.Select(d => $"{d.Index}:{d.Width}x{d.Height}@{d.Left},{d.Top}"));

                _screenInfo = screenInfo;

                var count = displays.Count;
                StatusText.Text = count > 1
                    ? $"{count} ekran eş zamanlı akıyor (Soldan sağa sıralı)."
                    : $"Uzak Çözünürlük: {_screenInfo.Width} x {_screenInfo.Height}";

                if (fingerprint != _lastScreenInfoFingerprint || _displayImages.Count == 0)
                {
                    _lastScreenInfoFingerprint = fingerprint;
                    UpdateDisplaySelectorUI(displays);
                    BuildMultiScreenLayout();
                }
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
            Header = $"Tüm ekranlar ({displays.Count})",
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
                Header = $"{d.Name} ({d.Width}x{d.Height})",
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

    private string _currentQualityMode = "auto";
    private string _currentQualityLabel = "⚡ Otomatik";

    private void IslandQualityBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private async void QualityAuto_Click(object sender, RoutedEventArgs e)
    {
        await SetQualityProfileAsync("auto", "⚡ Otomatik (Ağ Uyumlu)", QualityAutoItem);
    }

    private async void QualitySpeed_Click(object sender, RoutedEventArgs e)
    {
        await SetQualityProfileAsync("speed", "🚀 Hız & Düşük Gecikme", QualitySpeedItem);
    }

    private async void QualityBalanced_Click(object sender, RoutedEventArgs e)
    {
        await SetQualityProfileAsync("balanced", "⚖️ Dengeli Standart", QualityBalancedItem);
    }

    private async void QualityCrystal_Click(object sender, RoutedEventArgs e)
    {
        await SetQualityProfileAsync("quality", "💎 Kristal Netlik", QualityCrystalItem);
    }

    private async Task SetQualityProfileAsync(string mode, string label, MenuItem selectedItem)
    {
        _currentQualityMode = mode;
        _currentQualityLabel = label;

        if (QualityAutoItem != null) QualityAutoItem.IsChecked = (selectedItem == QualityAutoItem);
        if (QualitySpeedItem != null) QualitySpeedItem.IsChecked = (selectedItem == QualitySpeedItem);
        if (QualityBalancedItem != null) QualityBalancedItem.IsChecked = (selectedItem == QualityBalancedItem);
        if (QualityCrystalItem != null) QualityCrystalItem.IsChecked = (selectedItem == QualityCrystalItem);

        if (IslandQualityBtn != null)
        {
            IslandQualityBtn.Content = mode switch
            {
                "speed" => "🚀 Hız",
                "balanced" => "⚖️ Dengeli",
                "quality" => "💎 Kristal",
                _ => "⚡ Kalite"
            };
        }

        if (_sessionId is not null && _connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("SendSignal", _sessionId.Value, "set-quality-mode", mode);
                StatusText.Text = $"Kalite profili güncellendi: {label}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Kalite profili ayarlanamadı: {ex.Message}";
            }
        }
    }

    private void BuildMultiScreenLayout()
    {
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

        // Check if existing elements can be updated in-place without tearing down visual tree
        var visibleIndices = visibleDisplays.Select(d => d.Index).ToHashSet();
        var existingIndices = _displayImages.Keys.ToHashSet();

        if (visibleIndices.SetEquals(existingIndices) && MultiScreenPanel.Children.Count == visibleDisplays.Count)
        {
            // Fast in-place layout update (Zero flicker, zero focus drop)
            for (int i = 0; i < visibleDisplays.Count; i++)
            {
                var display = visibleDisplays[i];
                var displayIndex = display.Index;
                if (!_displayImages.TryGetValue(displayIndex, out var image)) continue;

                double tileWidth, tileHeight;
                if (_currentStretch == Stretch.None)
                {
                    tileWidth = display.Width;
                    tileHeight = display.Height;
                }
                else if (isSingleView)
                {
                    tileWidth = availWidth;
                    tileHeight = availHeight;
                }
                else
                {
                    var aspect = display.Height > 0 ? display.Width / (double)display.Height : 16.0 / 9.0;
                    var totalScreens = visibleDisplays.Count;
                    var maxPerScreenWidth = Math.Max(320, (availWidth - (totalScreens * 6)) / totalScreens);
                    tileHeight = Math.Min(availHeight, maxPerScreenWidth / aspect);
                    tileWidth = tileHeight * aspect;
                }

                image.Width = tileWidth;
                image.Height = tileHeight;
                image.Stretch = _currentStretch;

                if (MultiScreenPanel.Children[i] is Border border)
                {
                    border.Margin = isSingleView ? new Thickness(0) : new Thickness(2);
                    border.BorderThickness = isSingleView ? new Thickness(0) : new Thickness(1);
                    border.CornerRadius = isSingleView ? new CornerRadius(0) : new CornerRadius(6);
                    if (border.Child is Grid grid)
                    {
                        grid.Width = tileWidth;
                        grid.Height = tileHeight;
                        if (grid.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock label)
                        {
                            label.Visibility = isSingleView ? Visibility.Collapsed : Visibility.Visible;
                        }
                    }
                }
            }
            return;
        }

        var existingSources = new Dictionary<int, ImageSource>();
        foreach (var (idx, img) in _displayImages)
        {
            if (img.Source != null)
            {
                existingSources[idx] = img.Source;
            }
        }

        MultiScreenPanel.Children.Clear();
        _displayImages.Clear();
        _lastRemotePointPerDisplay.Clear();

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
            else if (isSingleView)
            {
                tileWidth = availWidth;
                tileHeight = availHeight;
            }
            else
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
                SnapsToDevicePixels = true,
                Source = existingSources.GetValueOrDefault(display.Index)
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

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
                Visibility = isSingleView ? Visibility.Collapsed : Visibility.Visible,
                IsHitTestVisible = false
            };

            var tileGrid = new Grid
            {
                Width = tileWidth,
                Height = tileHeight,
                Background = Brushes.Transparent,
                Focusable = true,
                Cursor = Cursors.Arrow
            };
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
                Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
                Focusable = true
            };

            AttachRemoteInputHandlers(tileBorder, displayIndex, image);

            _displayImages[display.Index] = image;
            MultiScreenPanel.Children.Add(tileBorder);
        }
    }

    private void AttachRemoteInputHandlers(UIElement target, int displayIndex, Image image)
    {
        target.PreviewMouseMove += (s, e) => HandleTileMouseMove(displayIndex, image, e);
        target.PreviewMouseLeftButtonDown += (s, e) => { FocusRemoteInputSurface(target); HandleTileMouseButton(displayIndex, image, e, "left", true); e.Handled = true; };
        target.PreviewMouseLeftButtonUp += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "left", false); e.Handled = true; };
        target.PreviewMouseRightButtonDown += (s, e) => { FocusRemoteInputSurface(target); HandleTileMouseButton(displayIndex, image, e, "right", true); e.Handled = true; };
        target.PreviewMouseRightButtonUp += (s, e) => { HandleTileMouseButton(displayIndex, image, e, "right", false); e.Handled = true; };
        target.PreviewMouseDown += (s, e) =>
        {
            FocusRemoteInputSurface(target);
            if (e.ChangedButton == MouseButton.Middle)
            {
                HandleTileMouseButton(displayIndex, image, e, "middle", true);
                e.Handled = true;
            }
        };
        target.PreviewMouseUp += (s, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                HandleTileMouseButton(displayIndex, image, e, "middle", false);
                e.Handled = true;
            }
        };
        target.PreviewMouseWheel += (s, e) =>
        {
            FocusRemoteInputSurface(target);
            _ = SendRemoteInputAsync(new RemoteInputEvent(_sessionId ?? Guid.Empty, "mouse-wheel", WheelDelta: e.Delta, DisplayIndex: displayIndex));
            e.Handled = true;
        };
    }

    private void FocusRemoteInputSurface(UIElement target)
    {
        RemoteScrollViewer.Focus();
        target.Focus();
        Keyboard.Focus(target);
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
            var clampedX = Math.Clamp(imageX, 0, meta.Width);
            var clampedY = Math.Clamp(imageY, 0, meta.Height);
            x = (int)Math.Round(clampedX);
            y = (int)Math.Round(clampedY);
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

            var clampedX = Math.Clamp(imageX, 0, renderedWidth);
            var clampedY = Math.Clamp(imageY, 0, renderedHeight);

            x = (int)Math.Round(clampedX / scale);
            y = (int)Math.Round(clampedY / scale);
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

    private async Task ProcessIncomingFrameAsync(string payload)
    {
        try
        {
            var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
            var frame = JsonSerializer.Deserialize<MultiScreenFrame>(payload);
            if (frame is null || string.IsNullOrEmpty(frame.JpegBase64))
            {
                return;
            }

            // Decode JPEG on background thread pool (Zero UI Dispatcher load!)
            var bytes = Convert.FromBase64String(frame.JpegBase64);
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            await Dispatcher.BeginInvoke(() =>
            {
                ApplyRenderedFrame(frame, bitmap, payloadByteCount);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.BeginInvoke(() => StatusText.Text = $"Kare çözme hatası: {ex.Message}");
        }
    }

    private void ApplyRenderedFrame(MultiScreenFrame frame, BitmapImage bitmap, int payloadByteCount)
    {
        if (!_displayImages.TryGetValue(frame.DisplayIndex, out var image) || image is null)
        {
            if (_displayImages.Count == 1 && frame.DisplayIndex == 0)
            {
                image = _displayImages.Values.FirstOrDefault();
            }
            else if (_displayImages.Count == 0)
            {
                image = CreateFallbackDisplayImage(frame.DisplayIndex);
            }
        }

        if (image is null) return;

        _lastFrameReceivedTicks = Stopwatch.GetTimestamp();
        _bytesReceived += payloadByteCount;
        _framesPerDisplay[frame.DisplayIndex] = _framesPerDisplay.GetValueOrDefault(frame.DisplayIndex) + 1;

        image.Source = bitmap;

        if (PlaceholderPanel.Visibility == Visibility.Visible)
        {
            PlaceholderPanel.Visibility = Visibility.Collapsed;
        }

        CalculateMetrics();
        _ = SendFrameAckAsync(frame);
    }

    private Image CreateFallbackDisplayImage(int displayIndex)
    {
        var image = new Image
        {
            Stretch = _currentStretch,
            Width = Math.Max(800, RemoteScrollViewer.ActualWidth > 0 ? RemoteScrollViewer.ActualWidth : 1280),
            Height = Math.Max(600, RemoteScrollViewer.ActualHeight > 0 ? RemoteScrollViewer.ActualHeight : 720),
            Focusable = true,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);

        var tileGrid = new Grid
        {
            Background = Brushes.Transparent,
            Focusable = true,
            Cursor = Cursors.Arrow
        };
        tileGrid.Children.Add(image);

        var tileBorder = new Border
        {
            Child = tileGrid,
            CornerRadius = new CornerRadius(0),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x00)),
            Focusable = true
        };

        AttachRemoteInputHandlers(tileBorder, displayIndex, image);
        _displayImages[displayIndex] = image;
        _displayMeta[displayIndex] = new DisplayItem(displayIndex, "Ekran", 1920, 1080, 0, 0);
        MultiScreenPanel.Children.Add(tileBorder);
        return image;
    }

    private async void RefreshScreenBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentConnectedDeviceId != Guid.Empty && (_connection == null || _connection.State != HubConnectionState.Connected || _isAwaitingReboot || PlaceholderPanel.Visibility == Visibility.Visible))
        {
            StatusText.Text = "Cihaza yeniden bağlanılıyor...";
            PlaceholderPanel.Visibility = Visibility.Visible;
            PlaceholderTitle.Text = "Cihaza Yeniden Bağlanılıyor";
            PlaceholderText.Text = "Canlı oturum yeniden kuruluyor, lütfen bekleyin...";
            await InitiateRemoteSessionAsync(_currentConnectedDeviceId);
            return;
        }

        if (_sessionId.HasValue && _connection?.State == HubConnectionState.Connected)
        {
            StatusText.Text = "Ekran akışı ve bilgisi yenileniyor...";
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

    private async Task SendFrameAckAsync(MultiScreenFrame frame)
    {
        if (frame.Sequence <= 0 || _sessionId is null || _connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var ack = new FrameAck(_sessionId.Value, frame.DisplayIndex, frame.Sequence, now, now);
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "frame-ack", JsonSerializer.Serialize(ack));
        }
        catch
        {
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
                CheckUpdatesBtn.Content = "Kontrol ediliyor...";
            }

            using var http = NexMoteHttp.CreateClient();
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
                    StatusText.Text = "Güncelleme MSI paketi indiriliyor...";
                    var tempMsi = Path.Combine(Path.GetTempPath(), "NexMote-Technician-Update.msi");
                    var msiBytes = await http.GetByteArrayAsync(downloadUrl);
                    await File.WriteAllBytesAsync(tempMsi, msiBytes);

                    StatusText.Text = "Yükleyici çalıştırılıyor (yönetici izni gerekebilir)...";
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
                CheckUpdatesBtn.Content = "Güncellemeyi kontrol et";
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

            var perDisplay = _framesPerDisplay.Count == 0
                ? $"{_displayImages.Count} ekran"
                : string.Join(" ", _framesPerDisplay.OrderBy(x => x.Key).Select(x => $"E{x.Key}:{Math.Round(x.Value / elapsedSeconds, 1)}"));
            FpsText.Text = $"FPS: {fps} | {perDisplay}";
            ThroughputText.Text = $"Hat: {kbps} KB/s";
            if (SessionStatsText != null)
            {
                var health = _smoothedLatencyMs switch
                {
                    <= 30 => "Mükemmel",
                    <= 75 => "İyi",
                    <= 140 => "Orta",
                    _ => "Zayıf"
                };
                var modeTag = _currentQualityMode switch
                {
                    "speed" => "🚀 Hız",
                    "balanced" => "⚖️ Dengeli",
                    "quality" => "💎 Kristal",
                    _ => "⚡ Oto"
                };
                SessionStatsText.Text = $"{fps} FPS · {kbps} KB/s · {Math.Round(_smoothedLatencyMs)} ms ({health}) · {modeTag}";
            }

            _frameCount = 0;
            _bytesReceived = 0;
            _framesPerDisplay.Clear();
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
            if (action.StartsWith("reboot", StringComparison.OrdinalIgnoreCase))
            {
                _isAwaitingReboot = true;
                PlaceholderPanel.Visibility = Visibility.Visible;
                PlaceholderTitle.Text = "Uzak Bilgisayar Yeniden Başlatılıyor";
                PlaceholderText.Text = "Cihaz ile bağlantı kesildi. Windows açıldığında oturum otomatik olarak bağlanacaktır.";
                StatusText.Text = "Uzak bilgisayar yeniden başlatılıyor. Otomatik yeniden bağlanma devrede...";
            }

            var req = new PowerActionRequest(_sessionId.Value, action);
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "power-action", JsonSerializer.Serialize(req));
            StatusText.Text = $"{label} komutu iletildi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Güç işlemi başarısız: {ex.Message}";
        }
    }

    private async Task StartRebootRecoveryWatchdogAsync(Guid deviceId)
    {
        _rebootWatchdogCts?.Cancel();
        _rebootWatchdogCts?.Dispose();
        _rebootWatchdogCts = new CancellationTokenSource();
        var ct = _rebootWatchdogCts.Token;

        var maxWaitSeconds = 300; // 5 dakika bekleme süresi
        var startTimestamp = Stopwatch.GetTimestamp();

        // 7 saniye cihazın kapanmasını bekle
        try
        {
            await Task.Delay(7000, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            var elapsedSec = (Stopwatch.GetTimestamp() - startTimestamp) / Stopwatch.Frequency;
            if (elapsedSec > maxWaitSeconds)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = "Yeniden başlatma zaman aşımına uğradı (5 dk).";
                    _isAwaitingReboot = false;
                    DisconnectBtn_Click(this, new RoutedEventArgs());
                });
                break;
            }

            try
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusText.Text = $"Uzak cihazın açılması bekleniyor ({elapsedSec} sn)...";
                    PlaceholderTitle.Text = "Uzak Cihazın Açılması Bekleniyor";
                    PlaceholderText.Text = $"Cihaz yeniden başlatılıyor ({elapsedSec} sn)...\nAçıldığı an Windows giriş ekranı otomatik bağlanacaktır.";
                });

                var response = await _http.GetAsync($"{_serverUrl.TrimEnd('/')}/api/devices/{deviceId}", ct);
                if (response.IsSuccessStatusCode)
                {
                    var dev = await response.Content.ReadFromJsonAsync<DeviceSummary>(cancellationToken: ct);
                    if (dev != null && dev.IsOnline)
                    {
                        // Cihaz çevrimiçi oldu!
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            StatusText.Text = "Cihaz çevrimiçi oldu! Canlı oturum otomatik başlatılıyor...";
                            PlaceholderTitle.Text = "Cihaz Çevrimiçi Oldu";
                            PlaceholderText.Text = "Windows giriş ekranı canlı olarak bağlanıyor, lütfen bekleyin...";
                            _isAwaitingReboot = false;
                            await Task.Delay(1500); // Ajanın soket odasına katılması için kısa tolerans
                            await InitiateRemoteSessionAsync(deviceId);
                        });
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch { }

            try
            {
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async void SendSasBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionId is null || _connection?.State != HubConnectionState.Connected) return;

        try
        {
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "send-sas", "{}");
            StatusText.Text = "Ctrl+Alt+Del (SAS) sinyali iletildi.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"SAS gönderilemedi: {ex.Message}";
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
        _rebootWatchdogCts?.Cancel();
        _isAwaitingReboot = false;
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
            input = input with
            {
                Sequence = ++_remoteInputSequence,
                SentAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            var payload = JsonSerializer.Serialize(input);
            await _connection.InvokeAsync("SendSignal", _sessionId.Value, "remote-input", payload);
            _remoteInputSentCount++;
            if (_remoteInputSentCount <= 3 || _remoteInputSentCount % 100 == 0)
            {
                await Dispatcher.BeginInvoke(() => StatusText.Text = $"Uzak girdi gönderiliyor ({_remoteInputSentCount})...");
            }
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

        const bool runAsAdmin = true;
        _pendingCommandRequestId = Guid.NewGuid().ToString("N");
        CommandOutputBox.Text += $"> [Yönetici] {command}{Environment.NewLine}";
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
            if (result is null || (_sessionId.HasValue && result.SessionId != _sessionId.Value))
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
                ? $"[Yönetici izni reddedildi veya UAC istemi zaman aşımına uğradı]{Environment.NewLine}{Environment.NewLine}"
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
    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsOnline { get; set; }

    public string StatusLabel => IsOnline ? "Çevrimiçi" : "Çevrimdışı";
    public string StatusBadgeBg => IsOnline ? "#ECFDF5" : "#F1F5F9";
    public string StatusBadgeFg => IsOnline ? "#047857" : "#64748B";
    public Brush StatusDotBrush => IsOnline
        ? new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81))
        : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));

    public string CpuText => $"CPU %{Math.Round(CpuUsagePercent, 0)}";
    public string MemoryUsageText
    {
        get
        {
            if (MemoryTotalMb <= 0)
            {
                return "RAM --";
            }

            var percent = Math.Round(MemoryUsedMb * 100.0 / MemoryTotalMb, 0);
            return $"RAM %{percent}";
        }
    }

    public string AgentVersionLabel => string.IsNullOrWhiteSpace(AgentVersion) ? "-" : $"v{AgentVersion}";

    public string LastSeenLabel
    {
        get
        {
            if (IsOnline)
            {
                return "şimdi";
            }

            if (LastSeenAt == default)
            {
                return "-";
            }

            var local = LastSeenAt.ToLocalTime();
            return local.ToString("HH:mm");
        }
    }

    public string StatusText => IsOnline ? "Çevrimiçi" : "Çevrimdışı";
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
