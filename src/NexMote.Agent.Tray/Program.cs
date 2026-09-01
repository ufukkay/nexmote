using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using NexMote.Shared.Contracts;
using NexMote.Shared.Network;
using NexMote.Shared.Telemetry;

namespace NexMote.Agent.Tray;

/// <summary>
/// Windows Sistem Tepsisi (Tray) uygulamasının ana başlangıç sınıfı.
/// DPI farkındalığını yapılandırır ve komut satırı parametresine göre normal Tepsi GUI veya SYSTEM yetkili Girdi Yardımcısı (--input-helper) modunda çalışır.
/// </summary>
internal static class Program
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public const int SW_RESTORE = 9;

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    /// <summary>
    /// Monitör başına DPI farkındalığını (Per-Monitor DPI Aware v2) etkinleştirir.
    /// </summary>
    public static void EnableDpiAwareness()
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        EnableDpiAwareness();
        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // SYSTEM yetkisinde çalışan Girdi Yardımcısı modu kontrolü (UAC tıklamaları için)
        if (args.Length > 0 && string.Equals(args[0], "--input-helper", StringComparison.OrdinalIgnoreCase))
        {
            InputHelperServer.Run();
            return;
        }

        // Giriş/Kilit Ekranı (Winlogon) için SYSTEM yetkili Canlı Oturum Yayıncısı modu
        if (args.Length > 0 && string.Equals(args[0], "--system-session", StringComparison.OrdinalIgnoreCase))
        {
            RunSystemSessionStreamer();
            return;
        }

        var explicitShow = args.Any(a => string.Equals(a, "--show", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "--dashboard", StringComparison.OrdinalIgnoreCase));
        // AGENTS.md Madde 2: Ajan asla kendiliğinden Durum Panelini açmamalı; yalnızca kullanıcı bilerek
        // --show/--dashboard ile başlattığında (kısayol, "Durum Panelini Aç" menüsü) açılır.
        var openDashboard = explicitShow;

        // Tekil Oturum Mutex Kontrolü (Her kullanıcı oturumunda en fazla 1 adet Agent Tray çalışabilir)
        var sessionId = Process.GetCurrentProcess().SessionId;
        var mutexName = $@"Global\NexMote_Agent_Tray_Session_{sessionId}";
        var eventName = $@"Global\NexMote_Agent_Tray_ShowDashboard_Session_{sessionId}";

        Mutex? mutex = null;
        bool createdNew;
        try
        {
            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var mutexSecurity = new MutexSecurity();
            mutexSecurity.AddAccessRule(new MutexAccessRule(worldSid, MutexRights.FullControl, AccessControlType.Allow));
            mutex = MutexAcl.Create(true, mutexName, out createdNew, mutexSecurity);
        }
        catch
        {
            mutex = new Mutex(true, mutexName, out createdNew);
        }

        if (!createdNew)
        {
            // Bu oturumda zaten çalışan bir Agent Tray mevcut.
            // Kullanıcı masaüstü kısayoluna tıkladıysa çalışan kopyaya Durum Panelini açması için sinyal gönder.
            if (openDashboard)
            {
                try
                {
                    if (EventWaitHandle.TryOpenExisting(eventName, out var showEvent))
                    {
                        showEvent.Set();
                    }
                }
                catch { }
            }
            mutex?.Dispose();
            return;
        }

        Application.Run(new TrayApplicationContext(openDashboardOnStart: openDashboard, eventName: eventName));
        mutex?.Dispose();
    }

    private static void RunSystemSessionStreamer()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var mutexName = $@"Global\NexMote_System_Session_Streamer_{sessionId}";

        Mutex? mutex = null;
        bool createdNew;
        try
        {
            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var mutexSecurity = new MutexSecurity();
            mutexSecurity.AddAccessRule(new MutexAccessRule(worldSid, MutexRights.FullControl, AccessControlType.Allow));
            mutex = MutexAcl.Create(true, mutexName, out createdNew, mutexSecurity);
        }
        catch
        {
            mutex = new Mutex(true, mutexName, out createdNew);
        }

        if (!createdNew)
        {
            mutex?.Dispose();
            return;
        }

        ApplicationConfiguration.Initialize();
        var serverUrl = AgentSettings.LoadServerUrl();
        var streamer = new RemoteScreenStreamer(serverUrl, _ => { });

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += async (_, _) =>
        {
            DesktopHelper.AttachToActiveDesktop();
            await streamer.EnsureStartedAsync();
        };
        timer.Start();

        _ = streamer.EnsureStartedAsync();

        // Girdi dinleyicisini de arka planda hazır tut
        var helperThread = new Thread(InputHelperServer.Run) { IsBackground = true };
        helperThread.Start();

        Application.Run();
        mutex?.Dispose();
    }
}

/// <summary>
/// Kullanıcı oturumunda arka planda çalışan sistem tepsisi (NotifyIcon) uygulama bağlamı.
/// Bildirim ikonu, sağ tık menüsü, servis kontrolü ve ekran yayıncısı (RemoteScreenStreamer) yaşam döngüsünü yönetir.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ServiceName = "NexMote Agent";
    private readonly NotifyIcon? _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _serverItem;
    private string _screenStatus = "hazirlaniyor";
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _signalingTimer;
    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private readonly CpuUsageSampler _cpuSampler = new();
    private readonly SynchronizationContext? _uiContext;
    private readonly RemoteScreenStreamer _streamer;
    private readonly EventWaitHandle? _showDashboardEvent;
    private readonly CancellationTokenSource _cts = new();
    private DashboardForm? _dashboardForm;
    private string _serverUrl;
    private readonly string _versionStr;
    private AgentSecurityProfileResponse? _securityProfile;
    private readonly System.Windows.Forms.Timer _securityProfileTimer;
    private Icon? _customTrayIcon;

    public TrayApplicationContext(bool openDashboardOnStart = false, string? eventName = null)
    {
        _versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";
        _uiContext = SynchronizationContext.Current;
        _serverUrl = AgentSettings.LoadServerUrl();
        _statusItem = new ToolStripMenuItem("Servis durumu: kontrol ediliyor") { Enabled = false };
        _serverItem = new ToolStripMenuItem($"Sunucu: {_serverUrl}") { Enabled = false };

        var menu = BuildContextMenu();

        try
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = IconHelper.GetAppIcon(),
                Text = $"NexMote Agent v{_versionStr}",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => ShowDashboardGated();
        }
        catch
        {
            // Running in session without interactive tray (e.g. Lock screen / Winlogon)
        }

        if (!string.IsNullOrEmpty(eventName))
        {
            try
            {
                var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var eventSecurity = new EventWaitHandleSecurity();
                eventSecurity.AddAccessRule(new EventWaitHandleAccessRule(worldSid, EventWaitHandleRights.FullControl, AccessControlType.Allow));
                _showDashboardEvent = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, eventName, out _, eventSecurity);
            }
            catch
            {
                try { _showDashboardEvent = new EventWaitHandle(false, EventResetMode.AutoReset, eventName); } catch { }
            }

            if (_showDashboardEvent != null)
            {
                var token = _cts.Token;
                Task.Run(() =>
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            if (_showDashboardEvent.WaitOne(500))
                            {
                                _uiContext?.Post(_ => ShowDashboard(), null);
                            }
                        }
                        catch { break; }
                    }
                }, token);
            }
        }

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 2000
        };
        _timer.Tick += (_, _) =>
        {
            RefreshStatus(showBalloon: false);
            _timer.Interval = 10000;
        };
        _timer.Start();

        _streamer = new RemoteScreenStreamer(_serverUrl, UpdateScreenStatus, RefreshSecurityProfileAsync);
        _signalingTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000 // Açılış anında 1 saniyelik agresif bağlantı kontrolü
        };
        _signalingTimer.Tick += async (_, _) =>
        {
            await _streamer.EnsureStartedAsync();
            _signalingTimer.Interval = _streamer.IsConnected ? 5000 : 1000;
        };
        _signalingTimer.Start();

        // 15 saniyelik periyodik canlılık (heartbeat) ve telemetri zamanlayıcısı
        _heartbeatTimer = new System.Windows.Forms.Timer
        {
            Interval = 500 // Açılışta derhal ilk heartbeat'i ilet
        };
        _heartbeatTimer.Tick += async (_, _) =>
        {
            await SendHeartbeatAsync();
            _heartbeatTimer.Interval = 15000;
        };
        _heartbeatTimer.Start();

        // 60 saniyelik periyodik güvenlik profili (branding + şifre koruması bayrakları) kontrolü
        _securityProfileTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _securityProfileTimer.Tick += async (_, _) =>
        {
            await RefreshSecurityProfileAsync();
            _securityProfileTimer.Interval = 60000;
        };
        _securityProfileTimer.Start();

        RefreshStatus(showBalloon: false);
        _ = _streamer.EnsureStartedAsync();
        _ = SendHeartbeatAsync();

        if (openDashboardOnStart)
        {
            ShowDashboard();
        }

        // Açılıştan 4 saniye sonra sessizce arka planda sunucu sürüm kontrolü yap
        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            await CheckForAgentUpdatesAsync(isManual: false);
        });
    }

    /// <summary>
    /// Kurumsal güvenlik profili menü/branding bileşenlerini <see cref="_securityProfile"/>'a göre kurar.
    /// Profil yoksa (veya kısıtlama kapalıysa) mevcut tam menü davranışı aynen korunur.
    /// </summary>
    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("🛡️ Durum Paneli", null, (_, _) => ShowDashboardGated());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => RequestExit());

        return menu;
    }

    /// <summary>Sunucudan bu cihaza atanmış güvenlik profilini çeker, branding/menüyü UI thread'inde günceller.</summary>
    private async Task RefreshSecurityProfileAsync()
    {
        try
        {
            var identity = DeviceIdentityFile.Load();
            if (identity is null) return;

            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
            var url = $"{_serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/security-profile?agentToken={Uri.EscapeDataString(identity.AgentToken)}";
            var response = await http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var profile = await response.Content.ReadFromJsonAsync<AgentSecurityProfileResponse>();
            if (profile is null) return;

            _uiContext?.Post(_ =>
            {
                _securityProfile = profile;
                _streamer.SetSecurityProfile(profile);
                ApplyBranding();
                if (_notifyIcon != null)
                {
                    _notifyIcon.ContextMenuStrip = BuildContextMenu();
                }
            }, null);
        }
        catch
        {
            // Sessizce geç — profil bilgisi al(a)mazsak varsayılan (kısıtlamasız) davranışla devam edilir.
        }
    }

    /// <summary>NotifyIcon metnini/ikonunu güvenlik profilindeki branding'e göre günceller.</summary>
    private void ApplyBranding()
    {
        if (_notifyIcon is null) return;

        var displayName = string.IsNullOrWhiteSpace(_securityProfile?.AgentDisplayName)
            ? "NexMote Agent"
            : _securityProfile!.AgentDisplayName!;
        var text = $"{displayName} v{_versionStr}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;

        var previousCustomIcon = _customTrayIcon;
        if (!string.IsNullOrWhiteSpace(_securityProfile?.IconBase64))
        {
            var decoded = DecodeIconFromBase64(_securityProfile!.IconBase64!);
            if (decoded is not null)
            {
                _customTrayIcon = decoded;
                _notifyIcon.Icon = decoded;
                previousCustomIcon?.Dispose();
                return;
            }
        }

        _notifyIcon.Icon = IconHelper.GetAppIcon();
        _customTrayIcon = null;
        previousCustomIcon?.Dispose();
    }

    private static Icon? DecodeIconFromBase64(string base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var ms = new MemoryStream(bytes);
            try
            {
                return new Icon(ms);
            }
            catch
            {
                ms.Position = 0;
                using var bmp = new Bitmap(ms);
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Durum Paneli, profil şifre istiyorsa önce sunucuda doğrulanmadan açılmaz.</summary>
    private async void ShowDashboardGated()
    {
        if (_securityProfile?.RequirePassword == true)
        {
            if (!await VerifyActionPasswordAsync("dashboard", "Durum Paneli", "Durum Panelini açmak için şifre girin:"))
            {
                return;
            }
        }

        ShowDashboard();
    }

    /// <summary>Ajanı kapatma (tray'den çıkış), profil şifre istiyorsa önce sunucuda doğrulanmadan yapılmaz.</summary>
    private async void RequestExit()
    {
        if (_securityProfile?.RequirePassword == true)
        {
            if (!await VerifyActionPasswordAsync("exit", "Ajanı Kapat", "Ajanı kapatmak için şifre girin:"))
            {
                return;
            }
        }

        ExitThread();
    }

    /// <summary>
    /// Kullanıcıdan şifre ister, sunucuda doğrular (<c>/api/agents/{id}/security/verify</c>). Yanlış şifrede
    /// tekrar sorar; ağ/sunucu hatasında veya kullanıcı iptal ederse false döner (fail-closed).
    /// </summary>
    private async Task<bool> VerifyActionPasswordAsync(string action, string title, string message)
    {
        var identity = DeviceIdentityFile.Load();
        if (identity is null)
        {
            MessageBox.Show("Cihaz kimliği bulunamadı, işlem yapılamıyor.", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        while (true)
        {
            var password = PromptForPassword(title, message);
            if (password is null) return false;

            try
            {
                using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
                var url = $"{_serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/security/verify";
                var response = await http.PostAsJsonAsync(url, new SecurityVerifyRequest(identity.AgentToken, action, password));
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<SecurityVerifyResponse>();
                    if (result?.Ok == true)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                MessageBox.Show("Sunucuya bağlanılamadı, işlem yapılamıyor.", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            MessageBox.Show("Şifre hatalı.", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? PromptForPassword(string title, string message)
    {
        using var form = new Form
        {
            Text = title,
            Width = 380,
            Height = 170,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            Icon = IconHelper.GetAppIcon(),
            TopMost = true
        };

        var label = new Label { Text = message, Left = 16, Top = 16, Width = 336, Height = 40 };
        var textBox = new TextBox { Left = 16, Top = 58, Width = 336, PasswordChar = '●' };
        var okButton = new Button { Text = "Tamam", Left = 196, Top = 92, Width = 75, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "İptal", Left = 277, Top = 92, Width = 75, DialogResult = DialogResult.Cancel };

        form.Controls.Add(label);
        form.Controls.Add(textBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;

        return form.ShowDialog() == DialogResult.OK ? textBox.Text : null;
    }

    private async Task SendHeartbeatAsync()
    {
        var identity = DeviceIdentityFile.Load();
        if (identity is null)
        {
            var enrollKey = AgentSettings.LoadEnrollmentKey();
            identity = await DeviceIdentityFile.EnsureEnrolledAsync(_serverUrl, enrollKey);
            if (identity is null) return;
        }

        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(6));
            var heartbeatUrl = $"{_serverUrl.TrimEnd('/')}/api/agents/{identity.DeviceId}/heartbeat";
            var mem = SystemTelemetry.GetMemoryMetrics();
            var diskFree = SystemTelemetry.GetDiskFreeMb();
            var ip = SystemTelemetry.GetPrimaryIPv4Address();
            var cpu = _cpuSampler.GetAveragePercent();
            var uptime = (long)TimeSpan.FromMilliseconds(Environment.TickCount64).TotalSeconds;
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";

            var req = new DeviceHeartbeatRequest(
                identity.AgentToken,
                ActiveUser: SessionUserResolver.GetActiveSessionUserName(),
                IpAddress: ip,
                CpuUsagePercent: cpu,
                MemoryTotalMb: mem.TotalMb,
                MemoryUsedMb: mem.UsedMb,
                DiskFreeMb: diskFree,
                UptimeSeconds: uptime,
                AgentVersion: version);

            var res = await http.PostAsJsonAsync(heartbeatUrl, req);
            if (res.StatusCode == System.Net.HttpStatusCode.Unauthorized || res.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                DeviceIdentityFile.Delete();
                var enrollKey = AgentSettings.LoadEnrollmentKey();
                await DeviceIdentityFile.EnsureEnrolledAsync(_serverUrl, enrollKey);
            }
        }
        catch { }
    }

    private async Task CheckForAgentUpdatesAsync(bool isManual)
    {
        try
        {
            var checkUrl = $"{_serverUrl.TrimEnd('/')}/api/updates/check";
            using var http = NexMoteHttp.CreateClient();
            var json = await http.GetStringAsync(checkUrl);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("agent", out var agent) &&
                agent.TryGetProperty("version", out var verProp) &&
                agent.TryGetProperty("downloadUrl", out var urlProp))
            {
                var latestVersion = verProp.GetString();
                var downloadUrl = urlProp.GetString();
                var releaseNotes = agent.TryGetProperty("releaseNotes", out var notesProp) ? notesProp.GetString() : "Performans ve kararlılık iyileştirmesi.";

                var runningVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";
                var isNewer = Version.TryParse(latestVersion, out var latest) &&
                              Version.TryParse(runningVer, out var current) &&
                              latest > current;

                if (!isNewer)
                {
                    if (isManual)
                    {
                        MessageBox.Show(
                            $"NexMote Ajanınız zaten en güncel sürümde (v{runningVer}).",
                            "Güncelleme Kontrolü",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                if (isManual)
                {
                    var prompt = MessageBox.Show(
                        $"NexMote Ajanı için yeni bir güncelleme mevcut!\n\n" +
                        $"Mevcut Sürüm: v{runningVer}\n" +
                        $"Yeni Sürüm: v{latestVersion}\n" +
                        $"Açıklama: {releaseNotes}\n\n" +
                        $"Ajan uygulamasını şimdi güncellemek istiyor musunuz?",
                        "NexMote Ajan Güncellemesi",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information);

                    if (prompt == DialogResult.Yes && !string.IsNullOrEmpty(downloadUrl))
                    {
                        using var progressForm = new UpdateProgressForm(downloadUrl, latestVersion ?? "0.7.0");
                        progressForm.ShowDialog();
                    }
                }
                else if (!string.IsNullOrEmpty(downloadUrl))
                {
                    // Arka plan otomatik güncellemesi: Kullanıcıyı rahatsız etmeden sessizce indir ve kur
                    _statusItem.Text = "Servis durumu: arka plan güncellemesi indiriliyor...";
                    await RemoteScreenStreamer.PerformSelfUpdateAsync(downloadUrl);
                }
            }
            else if (isManual)
            {
                MessageBox.Show("Ajanınız zaten en güncel sürümde.", "Güncelleme Kontrolü", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (isManual)
            {
                MessageBox.Show($"Güncelleme kontrolü başarısız: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void RefreshStatus(bool showBalloon)
    {
        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";
        var status = GetServiceStatus();
        _statusItem.Text = $"Servis durumu: {status}";
        if (_notifyIcon != null)
        {
            try
            {
                _notifyIcon.Text = $"NexMote Agent v{versionStr} ({status})";
                if (showBalloon)
                {
                    _notifyIcon.BalloonTipTitle = $"NexMote Agent v{versionStr}";
                    _notifyIcon.BalloonTipText = $"Servis durumu: {status}";
                    _notifyIcon.ShowBalloonTip(2500);
                }
            }
            catch { }
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

    private void ShowDashboard()
    {
        RefreshStatus(showBalloon: false);

        if (_dashboardForm is { IsDisposed: false })
        {
            if (_dashboardForm.WindowState == FormWindowState.Minimized)
            {
                _dashboardForm.WindowState = FormWindowState.Normal;
            }
            _dashboardForm.RefreshState();
            _dashboardForm.Show();
            _dashboardForm.BringToFront();
            _dashboardForm.Activate();
            if (_dashboardForm.Handle != IntPtr.Zero)
            {
                Program.ShowWindow(_dashboardForm.Handle, Program.SW_RESTORE);
                Program.SetForegroundWindow(_dashboardForm.Handle);
            }
            return;
        }

        _dashboardForm = new DashboardForm(
            getIsConnected: () => _streamer.IsConnected,
            refresh: () => RefreshStatus(showBalloon: false));
        _dashboardForm.Show();
        _dashboardForm.WindowState = FormWindowState.Normal;
        _dashboardForm.BringToFront();
        _dashboardForm.Activate();
        if (_dashboardForm.Handle != IntPtr.Zero)
        {
            Program.ShowWindow(_dashboardForm.Handle, Program.SW_RESTORE);
            Program.SetForegroundWindow(_dashboardForm.Handle);
        }
    }

    private void UpdateScreenStatus(string status)
    {
        _screenStatus = status;
    }

    private void OpenWebPanel()
    {
        try
        {
            var uri = new Uri(_serverUrl);
            var isLocal = string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase);
            var panelUri = isLocal
                ? $"{uri.Scheme}://{uri.Host}:5173/"
                : _serverUrl.TrimEnd('/');
            Process.Start(new ProcessStartInfo(panelUri) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show("Panel adresi açılamadı.", "NexMote Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowServerSettingsDialog()
    {
        using var form = new Form
        {
            Width = 460,
            Height = 220,
            Text = "NexMote Agent - Sunucu Adresi",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9F)
        };

        var lblUrl = new Label { Left = 20, Top = 18, Width = 400, Text = "NexMote Yönetim Sunucusu Adresi (URL):", ForeColor = Color.FromArgb(0x64, 0x74, 0x8B), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
        var txtUrl = new TextBox { Left = 20, Top = 45, Width = 400, Text = _serverUrl, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9.5F) };

        var lblInfo = new Label { Left = 20, Top = 80, Width = 400, Text = "💡 Sıfır-Kodlu Kurulum: Ajan bu sunucuya otomatik ve güvenle kaydolur.", ForeColor = Color.FromArgb(0x94, 0xA3, 0xB8), Font = new Font("Segoe UI", 8.5F) };

        var btnSave = new Button { Left = 210, Top = 120, Width = 100, Height = 35, Text = "Kaydet", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x25, 0x63, 0xEB), ForeColor = Color.White };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button { Left = 320, Top = 120, Width = 100, Height = 35, Text = "İptal", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0xF1, 0xF5, 0xF9), ForeColor = Color.FromArgb(0x33, 0x41, 0x55) };
        btnCancel.FlatAppearance.BorderSize = 0;

        form.Controls.AddRange(new Control[] { lblUrl, txtUrl, lblInfo, btnSave, btnCancel });
        form.AcceptButton = btnSave;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK)
        {
            var newUrl = NexMoteHttp.NormalizeUrl(txtUrl.Text);

            _serverUrl = newUrl;
            AgentSettings.SaveSettings(newUrl, AgentSettings.LoadEnrollmentKey());
            _serverItem.Text = $"Sunucu: {newUrl}";
            _streamer.UpdateServerUrl(newUrl);
            MessageBox.Show($"Sunucu adresi güncellendi:\nURL: {newUrl}\n\nYeni sunucuya bağlanılıyor...", "NexMote Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override void ExitThreadCore()
    {
        _cts.Cancel();
        _cts.Dispose();
        _showDashboardEvent?.Dispose();
        _timer.Stop();
        _signalingTimer.Stop();
        _timer.Dispose();
        _signalingTimer.Dispose();
        _dashboardForm?.Dispose();
        _streamer.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        base.ExitThreadCore();
    }
}
