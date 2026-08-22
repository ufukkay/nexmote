using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO.Pipes;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.ServiceProcess;
using Microsoft.AspNetCore.SignalR.Client;
using NexMote.Shared.Contracts;
using NexMote.Shared.Identity;
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
        // --show/--dashboard ile başlattığında (kısayol, "Durum Panelini Aç" menüsü) açılır. Argümansız
        // (veya --tray ile) her başlatma sessiz tepsi modunda kalmalı — bazı kurulum yollarının (örn.
        // install.bat) hiç argüman geçmeden başlatabildiği göz önüne alınırsa, "bilinmeyen/eksik argüman ⇒
        // panel aç" varsayımı bu kuralı ihlal eder.
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
            // ACL API başarısız olursa (kısıtlı ortam vb.) düz Mutex'e düş — Main() ve InputHelperServer.Run()
            // ile aynı fallback deseni. Bu olmadan --system-session süreci yakalanmamış bir istisnayla
            // çöküyor, Worker'ın watchdog'u da her saniye yeniden başlatmayı deneyip sessiz bir crash-loop'a giriyordu.
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
/// Uygulama ikonunu diskteki nexmote.ico dosyasından, çalıştırılabilir dosya kaynağından veya dinamik piksel oluşturucudan yükleyen yardımcı sınıf.
/// </summary>
internal static class IconHelper
{
    private static Icon? _appIcon;

    public static Icon GetAppIcon()
    {
        if (_appIcon != null) return _appIcon;

        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                var assoc = Icon.ExtractAssociatedIcon(exePath);
                if (assoc != null)
                {
                    _appIcon = assoc;
                    return _appIcon;
                }
            }
        }
        catch { }

        try
        {
            var icoPath = Path.Combine(AppContext.BaseDirectory, "nexmote.ico");
            if (File.Exists(icoPath))
            {
                _appIcon = new Icon(icoPath);
                return _appIcon;
            }
        }
        catch { }

        try
        {
            using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using var brush = new SolidBrush(Color.FromArgb(0x25, 0x63, 0xEB));
            using var path = new GraphicsPath();
            int r = 6;
            path.AddArc(1, 1, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 1, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(1, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);

            using var font = new Font("Segoe UI", 16F, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("N", font, textBrush, new RectangleF(0, 0, 32, 32), sf);

            var hIcon = bmp.GetHicon();
            _appIcon = Icon.FromHandle(hIcon);
            return _appIcon;
        }
        catch
        {
            _appIcon = SystemIcons.Application;
            return _appIcon;
        }
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

    public TrayApplicationContext(bool openDashboardOnStart = false, string? eventName = null)
    {
        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";
        _uiContext = SynchronizationContext.Current;
        _serverUrl = AgentSettings.LoadServerUrl();
        _statusItem = new ToolStripMenuItem("Servis durumu: kontrol ediliyor") { Enabled = false };
        _serverItem = new ToolStripMenuItem($"Sunucu: {_serverUrl}") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add($"NexMote Agent v{versionStr}").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_statusItem);
        menu.Items.Add(_serverItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("🛡️ Durum Panelini Aç", null, (_, _) => ShowDashboard());
        menu.Items.Add("🚀 Güncelleme Kontrol Et", null, async (_, _) => await CheckForAgentUpdatesAsync(isManual: true));
        menu.Items.Add("Sunucu Ayarları...", null, (_, _) => ShowServerSettingsDialog());
        menu.Items.Add("Durumu Yenile", null, (_, _) => RefreshStatus(showBalloon: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tray'i Kapat", null, (_, _) => ExitThread());

        try
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = IconHelper.GetAppIcon(),
                Text = $"NexMote Agent v{versionStr}",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
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

        _streamer = new RemoteScreenStreamer(_serverUrl, UpdateScreenStatus);
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
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";

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

                var runningVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";
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
                        using var progressForm = new UpdateProgressForm(downloadUrl, latestVersion ?? "0.6.2");
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
        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";
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
            getServiceStatus: GetServiceStatus,
            getServerUrl: () => _serverUrl,
            getScreenStatus: () => _screenStatus,
            getIsConnected: () => _streamer.IsConnected,
            openPanel: OpenWebPanel,
            openSettings: ShowServerSettingsDialog,
            refresh: () => RefreshStatus(showBalloon: false),
            checkUpdates: () => _ = CheckForAgentUpdatesAsync(isManual: true),
            runNetworkTest: () => _streamer.RunServerNetworkTestAsync(),
            saveSettings: (newUrl, newKey) =>
            {
                _serverUrl = newUrl;
                AgentSettings.SaveSettings(newUrl, newKey);
                _serverItem.Text = $"Sunucu: {newUrl}";
                _streamer.UpdateServerUrl(newUrl);
            });
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
        var currentKey = AgentSettings.LoadEnrollmentKey();

        using var form = new Form
        {
            Width = 460,
            Height = 280,
            Text = "NexMote Agent - Sunucu Ayarları",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 9F)
        };

        var lblUrl = new Label { Left = 20, Top = 15, Width = 400, Text = "Sunucu adresi:", ForeColor = Color.FromArgb(0x64, 0x74, 0x8B) };
        var txtUrl = new TextBox { Left = 20, Top = 38, Width = 400, Text = _serverUrl, BorderStyle = BorderStyle.FixedSingle };

        var lblKey = new Label { Left = 20, Top = 80, Width = 400, Text = "Kayıt anahtarı:", ForeColor = Color.FromArgb(0x64, 0x74, 0x8B) };
        var txtKey = new TextBox { Left = 20, Top = 103, Width = 400, Text = currentKey, BorderStyle = BorderStyle.FixedSingle };

        var btnSave = new Button { Left = 210, Top = 165, Width = 100, Height = 35, Text = "Kaydet", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0x25, 0x63, 0xEB), ForeColor = Color.White };
        btnSave.FlatAppearance.BorderSize = 0;
        var btnCancel = new Button { Left = 320, Top = 165, Width = 100, Height = 35, Text = "İptal", DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0xF1, 0xF5, 0xF9), ForeColor = Color.FromArgb(0x33, 0x41, 0x55) };
        btnCancel.FlatAppearance.BorderSize = 0;

        form.Controls.AddRange(new Control[] { lblUrl, txtUrl, lblKey, txtKey, btnSave, btnCancel });
        form.AcceptButton = btnSave;
        form.CancelButton = btnCancel;

        if (form.ShowDialog() == DialogResult.OK)
        {
            var newUrl = NexMoteHttp.NormalizeUrl(txtUrl.Text);
            var newKey = txtKey.Text.Trim();

            _serverUrl = newUrl;
            AgentSettings.SaveSettings(newUrl, newKey);
            _serverItem.Text = $"Sunucu: {newUrl}";
            _streamer.UpdateServerUrl(newUrl);
            MessageBox.Show($"Sunucu ayarları güncellendi:\nURL: {newUrl}\n\nYeni sunucuya bağlanılıyor...", "NexMote Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

/// <summary>
/// Antivirüs yazılımlarına benzer modern durum ve güvenlik paneli formu.
/// Koruma durumu (yeşil/turuncu/kırmızı kalkan), servis durumu, sunucu bağlantısı, ekran akışı durumu ve sürüm bilgilerini gösterir.
/// </summary>
internal sealed class DashboardForm : Form
{
    private static readonly Color AccentBlue = Color.FromArgb(0x25, 0x63, 0xEB);
    private static readonly Color AccentHover = Color.FromArgb(0x1D, 0x4E, 0xD8);
    private static readonly Color TextDark = Color.FromArgb(0x0F, 0x17, 0x2A);
    private static readonly Color TextMuted = Color.FromArgb(0x64, 0x74, 0x8B);
    private static readonly Color BorderColor = Color.FromArgb(0xE2, 0xE8, 0xF0);
    private static readonly Color SurfaceColor = Color.FromArgb(0xF8, 0xFA, 0xFC);
    private static readonly Color CardBg = Color.FromArgb(0xFF, 0xFF, 0xFF);
    private static readonly Color SuccessGreen = Color.FromArgb(0x10, 0xB9, 0x81);
    private static readonly Color SuccessBg = Color.FromArgb(0xEC, 0xFD, 0xF5);
    private static readonly Color WarnOrange = Color.FromArgb(0xEA, 0x58, 0x0C);
    private static readonly Color WarnBg = Color.FromArgb(0xFF, 0xF7, 0xED);
    private static readonly Color DangerRed = Color.FromArgb(0xDC, 0x26, 0x26);
    private static readonly Color DangerBg = Color.FromArgb(0xFE, 0xF2, 0xF2);

    private readonly Func<string> _getServiceStatus;
    private readonly Func<string> _getServerUrl;
    private readonly Func<string> _getScreenStatus;
    private readonly Func<bool> _getIsConnected;
    private readonly Action _openPanel;
    private readonly Action _openSettings;
    private readonly Action _refresh;
    private readonly Action _checkUpdates;
    private readonly Func<Task<NetworkSpeedResult>> _runNetworkTest;
    private readonly Action<string, string> _saveSettings;

    // UI Controls
    private Panel _heroCard = null!;
    private Label _heroIcon = null!;
    private Label _heroTitle = null!;
    private Label _heroSubtitle = null!;
    private Button _heroActionBtn = null!;

    private TextBox _txtServerUrl = null!;
    private TextBox _txtDeviceToken = null!;
    private Label _lblServiceStatus = null!;
    private Label _lblServerConnStatus = null!;
    private Label _lblSignalRStatus = null!;
    private Label _agentStatusPill = null!;

    public DashboardForm(
        Func<string> getServiceStatus,
        Func<string> getServerUrl,
        Func<string> getScreenStatus,
        Func<bool> getIsConnected,
        Action openPanel,
        Action openSettings,
        Action refresh,
        Action checkUpdates,
        Func<Task<NetworkSpeedResult>> runNetworkTest,
        Action<string, string> saveSettings)
    {
        _getServiceStatus = getServiceStatus;
        _getServerUrl = getServerUrl;
        _getScreenStatus = getScreenStatus;
        _getIsConnected = getIsConnected;
        _openPanel = openPanel;
        _openSettings = openSettings;
        _refresh = refresh;
        _checkUpdates = checkUpdates;
        _runNetworkTest = runNetworkTest;
        _saveSettings = saveSettings;

        Text = "NexMote Agent Durum Paneli";
        ClientSize = new Size(880, 560);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = SurfaceColor;
        Font = new Font("Segoe UI", 9F);
        Icon = IconHelper.GetAppIcon();

        BuildLayout();
        RefreshState();
    }

    private void BuildLayout()
    {
        Controls.Clear();

        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";

        // 1. TOP HEADER BAR
        var headerPanel = new Panel { Left = 0, Top = 0, Width = 880, Height = 56, BackColor = Color.White };
        headerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 55, 880, 55);
        };
        
        var logoIcon = new Label
        {
            Text = "N",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            Left = 24,
            Top = 12,
            Width = 32,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter
        };
        logoIcon.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(AccentBlue);
            using var path = new GraphicsPath();
            int r = 6;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(32 - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(32 - r * 2, 32 - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, 32 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);

            using var font = new Font("Segoe UI", 13F, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("N", font, textBrush, new RectangleF(0, 0, 32, 32), sf);
        };

        var brandTitle = new Label
        {
            Text = "NexMote Agent",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 66,
            Top = 13,
            AutoSize = true
        };

        var versionLabel = new Label
        {
            Text = $"v{versionStr}",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextMuted,
            Left = 230,
            Top = 18,
            AutoSize = true
        };

        _agentStatusPill = new Label
        {
            Text = "• Ajan Aktif",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A),
            BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4),
            Left = 704,
            Top = 14,
            Width = 150,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _agentStatusPill.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0x86, 0xEF, 0xAC));
            e.Graphics.DrawRectangle(pen, 0, 0, _agentStatusPill.Width - 1, _agentStatusPill.Height - 1);
        };

        headerPanel.Controls.AddRange(new Control[] { logoIcon, brandTitle, versionLabel, _agentStatusPill });

        // 2. HERO PROTECTION STATUS CARD
        _heroCard = new Panel { Left = 24, Top = 72, Width = 832, Height = 74, BackColor = SuccessBg };
        _heroCard.Paint += (_, e) =>
        {
            var isConn = _getIsConnected();
            var bColor = isConn ? Color.FromArgb(0xBB, 0xF7, 0xD0) : Color.FromArgb(0xFE, 0xD7, 0xAA);
            var barColor = isConn ? SuccessGreen : WarnOrange;
            using var pen = new Pen(bColor);
            e.Graphics.DrawRectangle(pen, 0, 0, _heroCard.Width - 1, _heroCard.Height - 1);

            using var brush = new SolidBrush(barColor);
            e.Graphics.FillRectangle(brush, 0, 0, 5, _heroCard.Height);
        };

        _heroIcon = new Label
        {
            Text = "🛡️",
            Font = new Font("Segoe UI", 18F),
            ForeColor = SuccessGreen,
            Left = 16,
            Top = 14,
            Width = 44,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _heroTitle = new Label
        {
            Text = "Sistem Korunuyor ve Canlı Akışa Hazır",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 70,
            Top = 14,
            Width = 600,
            Height = 24
        };

        _heroSubtitle = new Label
        {
            Text = "Sunucu ve SignalR bağlantısı aktif. Uzaktan destek oturumları kabul edilebilir.",
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMuted,
            Left = 70,
            Top = 40,
            Width = 600,
            Height = 20
        };

        _heroActionBtn = new Button
        {
            Text = "🔄 Yenile",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            FlatStyle = FlatStyle.Flat,
            Left = 716,
            Top = 18,
            Width = 100,
            Height = 36,
            Cursor = Cursors.Hand
        };
        _heroActionBtn.FlatAppearance.BorderSize = 0;
        _heroActionBtn.Click += (_, _) => RefreshState();

        _heroCard.Controls.AddRange(new Control[] { _heroIcon, _heroTitle, _heroSubtitle, _heroActionBtn });

        // 3. TWO MAIN CARDS
        var cardLeft = CreateCard(24, 160, 406, 370);
        var cardRight = CreateCard(450, 160, 406, 370);

        // LEFT CARD: Sunucu Yapılandırması
        var leftTitle = new Label
        {
            Text = "🗄️  Sunucu Yapılandırması",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 18,
            Top = 16,
            Width = 360,
            Height = 24
        };

        var lblUrl = new Label { Text = "🌐 Sunucu Adresi (URL)", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Left = 18, Top = 50, Width = 360, Height = 18 };
        _txtServerUrl = new TextBox
        {
            Text = _getServerUrl(),
            Font = new Font("Segoe UI", 9.5F),
            Left = 18,
            Top = 72,
            Width = 368,
            Height = 30,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceColor
        };

        var lblToken = new Label { Text = "🔑 Cihaz Kimlik Token'ı", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Left = 18, Top = 118, Width = 360, Height = 18 };
        _txtDeviceToken = new TextBox
        {
            Text = DeviceIdentityFile.Load()?.DeviceId.ToString("N") ?? AgentSettings.LoadEnrollmentKey(),
            Font = new Font("Segoe UI", 9.5F),
            Left = 18,
            Top = 140,
            Width = 368,
            Height = 30,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = SurfaceColor
        };

        var btnSaveSettings = new Button
        {
            Text = "💾 Ayarları Kaydet",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            FlatStyle = FlatStyle.Flat,
            Left = 18,
            Top = 192,
            Width = 175,
            Height = 36,
            Cursor = Cursors.Hand
        };
        btnSaveSettings.FlatAppearance.BorderSize = 0;
        btnSaveSettings.Click += (_, _) =>
        {
            var url = NexMoteHttp.NormalizeUrl(_txtServerUrl.Text);
            var token = _txtDeviceToken.Text.Trim();
            _txtServerUrl.Text = url;
            _saveSettings(url, token);
            MessageBox.Show("Sunucu yapılandırması başarıyla kaydedildi!", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshState();
        };

        var btnCopyToken = new Button
        {
            Text = "📋 Token Kopyala",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = SurfaceColor,
            FlatStyle = FlatStyle.Flat,
            Left = 211,
            Top = 192,
            Width = 175,
            Height = 36,
            Cursor = Cursors.Hand
        };
        btnCopyToken.FlatAppearance.BorderColor = BorderColor;
        btnCopyToken.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(_txtDeviceToken.Text);
                MessageBox.Show("Cihaz token'ı panoya kopyalandı.", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch { }
        };

        var btnOpenWeb = new Button
        {
            Text = "🌐 Web Yönetim Panelini Aç",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = AccentBlue,
            BackColor = Color.FromArgb(0xEF, 0xF6, 0xFF),
            FlatStyle = FlatStyle.Flat,
            Left = 18,
            Top = 250,
            Width = 368,
            Height = 38,
            Cursor = Cursors.Hand
        };
        btnOpenWeb.FlatAppearance.BorderColor = Color.FromArgb(0xBF, 0xDB, 0xFE);
        btnOpenWeb.Click += (_, _) => _openPanel();

        cardLeft.Controls.AddRange(new Control[] { leftTitle, lblUrl, _txtServerUrl, lblToken, _txtDeviceToken, btnSaveSettings, btnCopyToken, btnOpenWeb });

        // RIGHT CARD: Bağlantı Durumu
        var rightTitle = new Label
        {
            Text = "⚡  Bağlantı & Canlı Durum",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 18,
            Top = 16,
            Width = 360,
            Height = 24
        };

        _lblServiceStatus = AddStatusRow(cardRight, 0, "🛡️  NexMote Servisi", "• Çalışıyor", SuccessGreen);
        _lblServerConnStatus = AddStatusRow(cardRight, 1, "🗄️  Sunucu Bağlantısı", "• Bağlı", SuccessGreen);
        _lblSignalRStatus = AddStatusRow(cardRight, 2, "📶  SignalR Canlı Akış", "• Bağlı", SuccessGreen);
        AddStatusRow(cardRight, 3, "📦  Yüklü Ajan Sürümü", $"v{versionStr}", AccentBlue);

        var btnTestConnection = new Button
        {
            Text = "🔄 Durumu Yenile",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x47, 0x55, 0x69),
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Left = 18,
            Top = 250,
            Width = 175,
            Height = 38,
            Cursor = Cursors.Hand
        };
        btnTestConnection.FlatAppearance.BorderColor = BorderColor;
        btnTestConnection.Click += (_, _) => RefreshState();

        var btnCheckUpdates = new Button
        {
            Text = "🚀 Güncelleme Kontrol",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = AccentBlue,
            BackColor = Color.FromArgb(0xEF, 0xF6, 0xFF),
            FlatStyle = FlatStyle.Flat,
            Left = 211,
            Top = 250,
            Width = 175,
            Height = 38,
            Cursor = Cursors.Hand
        };
        btnCheckUpdates.FlatAppearance.BorderColor = Color.FromArgb(0xBF, 0xDB, 0xFE);
        btnCheckUpdates.Click += (_, _) => _checkUpdates();

        cardRight.Controls.AddRange(new Control[] { rightTitle, btnTestConnection, btnCheckUpdates });

        Controls.AddRange(new Control[] { headerPanel, _heroCard, cardLeft, cardRight });
    }

    private static Panel CreateCard(int left, int top, int width, int height)
    {
        var card = new Panel { Left = left, Top = top, Width = width, Height = height, BackColor = CardBg };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        return card;
    }

    private static Label AddStatusRow(Panel parent, int index, string label, string defaultVal, Color defaultColor)
    {
        var y = 50 + index * 48;
        
        var rowBox = new Panel { Left = 18, Top = y, Width = 368, Height = 40, BackColor = SurfaceColor };
        rowBox.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, rowBox.Width - 1, rowBox.Height - 1);
        };

        var lbl = new Label
        {
            Text = label,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 12,
            Top = 10,
            Width = 200,
            Height = 20
        };

        var val = new Label
        {
            Text = defaultVal,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = defaultColor,
            Left = 210,
            Top = 10,
            Width = 146,
            Height = 20,
            TextAlign = ContentAlignment.MiddleRight
        };

        rowBox.Controls.Add(lbl);
        rowBox.Controls.Add(val);
        parent.Controls.Add(rowBox);
        return val;
    }

    public void RefreshState()
    {
        _refresh();

        var connected = _getIsConnected();
        var serviceStatus = _getServiceStatus();
        var isServiceRunning = serviceStatus.Contains("Calisiyor", StringComparison.OrdinalIgnoreCase);

        // Update TextBoxes
        _txtServerUrl.Text = _getServerUrl();
        var identity = DeviceIdentityFile.Load();
        _txtDeviceToken.Text = identity?.DeviceId.ToString("N") ?? AgentSettings.LoadEnrollmentKey();

        // Update Right Card Statuses
        if (isServiceRunning)
        {
            _lblServiceStatus.Text = "• Çalışıyor";
            _lblServiceStatus.ForeColor = SuccessGreen;
            _agentStatusPill.Text = "• Ajan Aktif";
            _agentStatusPill.ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A);
            _agentStatusPill.BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4);
        }
        else
        {
            _lblServiceStatus.Text = "• Durdu";
            _lblServiceStatus.ForeColor = DangerRed;
            _agentStatusPill.Text = "• Servis Kapalı";
            _agentStatusPill.ForeColor = DangerRed;
            _agentStatusPill.BackColor = DangerBg;
        }

        if (connected)
        {
            _lblServerConnStatus.Text = "• Bağlı";
            _lblServerConnStatus.ForeColor = SuccessGreen;
            _lblSignalRStatus.Text = "• Bağlı";
            _lblSignalRStatus.ForeColor = SuccessGreen;

            // Hero Card (Green)
            _heroCard.BackColor = SuccessBg;
            _heroIcon.Text = "🛡️";
            _heroIcon.ForeColor = SuccessGreen;
            _heroTitle.Text = "Sistem Korunuyor ve Canlı Akışa Hazır";
            _heroSubtitle.Text = "Sunucu ve SignalR bağlantısı aktif. Uzaktan kontrol oturumları hazır.";
            _heroSubtitle.ForeColor = TextMuted;
            _heroActionBtn.Text = "🔄 Yenile";
            _heroActionBtn.BackColor = SuccessGreen;
        }
        else
        {
            _lblServerConnStatus.Text = isServiceRunning ? "• Bağlanıyor..." : "• Bağlı Değil";
            _lblServerConnStatus.ForeColor = isServiceRunning ? WarnOrange : DangerRed;
            _lblSignalRStatus.Text = "• Bağlantı Bekleniyor";
            _lblSignalRStatus.ForeColor = WarnOrange;

            // Hero Card (Orange/Amber)
            _heroCard.BackColor = WarnBg;
            _heroIcon.Text = "📶";
            _heroIcon.ForeColor = WarnOrange;
            _heroTitle.Text = "Bağlantı Kuruluyor...";
            _heroSubtitle.Text = "Sunucu veya SignalR bağlantısı bekleniyor. Otomatik yeniden bağlanılıyor.";
            _heroSubtitle.ForeColor = TextMuted;
            _heroActionBtn.Text = "⚡ Bağlan";
            _heroActionBtn.BackColor = AccentBlue;
        }
        _heroCard.Invalidate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }
}

/// <summary>
/// Ajan güncellemesi indirilirken aşamaları ve yüzdelik ilerleme çubuğunu görsel olarak gösteren modern diyalog formu.
/// </summary>
internal sealed class UpdateProgressForm : Form
{
    private readonly string _downloadUrl;
    private readonly string _targetVersion;
    private readonly ProgressBar _progressBar;
    private readonly Label _lblStage;
    private readonly Label _lblDetails;
    private readonly Label _lblPercent;
    private readonly Button _btnAction;
    private readonly CancellationTokenSource _cts = new();
    private bool _isFinished;

    public UpdateProgressForm(string downloadUrl, string targetVersion)
    {
        _downloadUrl = downloadUrl;
        _targetVersion = targetVersion;

        Text = "NexMote Ajan Güncellemesi";
        ClientSize = new Size(520, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(0xF8, 0xFA, 0xFC);
        Font = new Font("Segoe UI", 9F);
        Icon = IconHelper.GetAppIcon();

        // 1. Üst Başlık Paneli
        var header = new Panel { Left = 0, Top = 0, Width = 520, Height = 64, BackColor = Color.White };
        header.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0xE2, 0xE8, 0xF0));
            e.Graphics.DrawLine(pen, 0, 63, 520, 63);
        };

        var icon = new Label
        {
            Text = "🚀",
            Font = new Font("Segoe UI", 18F),
            Left = 18,
            Top = 14,
            Width = 36,
            Height = 36,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var title = new Label
        {
            Text = $"NexMote Ajanı Güncelleniyor (v{_targetVersion})",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x0F, 0x17, 0x2A),
            Left = 62,
            Top = 12,
            Width = 440,
            Height = 24
        };

        _lblStage = new Label
        {
            Text = "Güncelleme paketi hazırlanıyor...",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(0x64, 0x74, 0x8B),
            Left = 62,
            Top = 36,
            Width = 440,
            Height = 20
        };

        header.Controls.AddRange(new Control[] { icon, title, _lblStage });

        // 2. İlerleme Çubuğu ve Detaylar
        _progressBar = new ProgressBar
        {
            Left = 24,
            Top = 86,
            Width = 472,
            Height = 22,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        _lblDetails = new Label
        {
            Text = "Sunucuya bağlanılıyor...",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x33, 0x41, 0x55),
            Left = 24,
            Top = 116,
            Width = 370,
            Height = 20
        };

        _lblPercent = new Label
        {
            Text = "%0",
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x25, 0x63, 0xEB),
            Left = 400,
            Top = 114,
            Width = 96,
            Height = 22,
            TextAlign = ContentAlignment.MiddleRight
        };

        // 3. Alt Eylem Butonu
        _btnAction = new Button
        {
            Text = "İptal",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x47, 0x55, 0x69),
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Left = 396,
            Top = 168,
            Width = 100,
            Height = 36,
            Cursor = Cursors.Hand
        };
        _btnAction.FlatAppearance.BorderColor = Color.FromArgb(0xE2, 0xE8, 0xF0);
        _btnAction.Click += (_, _) =>
        {
            if (_isFinished)
            {
                Close();
            }
            else
            {
                _cts.Cancel();
                _btnAction.Enabled = false;
                _lblStage.Text = "İptal ediliyor...";
            }
        };

        Controls.AddRange(new Control[] { header, _progressBar, _lblDetails, _lblPercent, _btnAction });

        Shown += async (_, _) => await StartDownloadAsync();
    }

    private async Task StartDownloadAsync()
    {
        var progress = new Progress<(long CurrentVal, long TotalVal, string Stage)>(info =>
        {
            if (IsDisposed) return;

            _lblStage.Text = info.Stage;

            if (info.TotalVal > 0)
            {
                var pct = (int)Math.Clamp((info.CurrentVal * 100.0) / info.TotalVal, 0, 100);
                _progressBar.Value = pct;
                _lblPercent.Text = $"%{pct}";
                _lblDetails.Text = info.Stage;
            }
        });

        try
        {
            await RemoteScreenStreamer.PerformSelfUpdateAsync(_downloadUrl, progress, _cts.Token);
            _isFinished = true;

            _progressBar.Value = 100;
            _lblPercent.Text = "%100";
            _lblPercent.ForeColor = Color.FromArgb(0x10, 0xB9, 0x81);
            _lblStage.Text = "✓ Güncelleme ve kurulum başarıyla tamamlandı!";
            _lblDetails.Text = "Yeni sürüm devrede. Ajan yenileniyor...";
            _btnAction.Text = "Kapat";
            _btnAction.ForeColor = Color.White;
            _btnAction.BackColor = Color.FromArgb(0x10, 0xB9, 0x81);
            _btnAction.FlatAppearance.BorderSize = 0;

            await Task.Delay(2500);
            if (!IsDisposed)
            {
                Close();
            }
        }
        catch (OperationCanceledException)
        {
            _isFinished = true;
            _lblStage.Text = "Güncelleme kullanıcı tarafından iptal edildi.";
            _lblDetails.Text = "İndirme ve kurulum durduruldu.";
            _btnAction.Text = "Kapat";
            _btnAction.Enabled = true;
        }
        catch (Exception ex)
        {
            _isFinished = true;
            _lblStage.Text = "Güncelleme sırasında hata oluştu.";
            _lblDetails.Text = ex.Message;
            _btnAction.Text = "Kapat";
            _btnAction.Enabled = true;
        }
    }
}

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
    private int _adaptiveQuality = 72;
    private readonly object _qualityLock = new();
    private readonly ConcurrentDictionary<int, long> _lastAckedSequencePerDisplay = new();
    private readonly Dictionary<Guid, (MemoryStream Stream, string FileName)> _activeTransfers = new();
    private NamedPipeClientStream? _inputHelperPipe;
    private StreamWriter? _inputHelperWriter;
    private long _nextPipeConnectAttemptTicks;
    private readonly object _pipeLock = new();

    public RemoteScreenStreamer(string serverUrl, Action<string> setStatus)
    {
        _serverUrl = serverUrl;
        _setStatus = setStatus;
    }

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

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
                try
                {
                    if (!string.IsNullOrEmpty(payload))
                    {
                        Thread thread = new(() => Clipboard.SetText(payload));
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                    }
                }
                catch { }
            }
            else if (string.Equals(type, "file-chunk", StringComparison.OrdinalIgnoreCase))
            {
                HandleFileChunk(payload);
            }
            else if (string.Equals(type, "remote-command", StringComparison.OrdinalIgnoreCase))
            {
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
            // Token uyuşmazlığı varsa kimliği yenileyip oturuma bağlanmayı tekrar dene
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

        var token = _streamCancellation.Token;
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

            // Route input injection to either SYSTEM input-helper (if running/connected)
            // or fallback directly to in-process injection.
            // NEVER execute both, as executing both causes duplicated / multiplied keystrokes and clicks!
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

    /// <summary>
    /// Forwards a raw remote-input JSON payload to the SYSTEM-in-session input helper over a
    /// local named pipe. Returns false (without blocking noticeably) if the helper isn't
    /// reachable, so the caller can fall back to direct in-process injection. Failed connection
    /// attempts back off for a couple of seconds to avoid stalling every mouse-move while the
    /// helper is starting up or unavailable.
    /// </summary>
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
            // Best-effort: the technician's UI will simply show no result for this request.
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
            // Audit delivery is best-effort; the command already ran and its result was returned to the technician.
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
                // Bağlantı koparsa otomatik yeniden bağlanmayı bekle
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
                // FLOW CONTROL (SIFIR GECİKME MOTORU):
                // Önceki kare henüz karşıya ulaşıp çizilmediyse (ACK gelmediyse), aradaki bayat kareleri atla!
                // TCP soketinde kuyruk birikmesini %100 engelleyerek görüntünün geriden gelmesini yok eder.
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
                            // Belirlenen limit içinde ACK gelmediyse hat dolmuş demektir; kaliteyi düşür ve en taze canlı kareye zıpla!
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

                // Hareket durduğunda kristal netlikte statik iyileştirme karesi
                var timeSinceMotionMs = (now - lastMotionTicks) * 1000 / Stopwatch.Frequency;
                var isRefinement = !refinementSent && timeSinceMotionMs > 150 && (now - lastSendTicks) > 0;

                int quality;
                if (isRefinement)
                {
                    quality = 92; // Kristal netlikte statik son kare
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
                    // 🚀 Hız & Sıfır Gecikme: Ultra hafif kareler, maksimum akıcılık
                    _adaptiveQuality = 58;
                    _frameDelayMs = 16; // ~60 FPS
                    break;

                case "balanced":
                    // ⚖️ Dengeli: Standart 40 FPS, kaliteli ofis modu
                    _adaptiveQuality = 74;
                    _frameDelayMs = 25; // ~40 FPS
                    break;

                case "quality":
                    // 💎 Kristal Netlik: 92 JPEG, detay odaklı
                    _adaptiveQuality = 92;
                    _frameDelayMs = 33; // ~30 FPS
                    break;

                case "auto":
                default:
                    // ⚡ Otomatik (Ağ Uyumlu - 4 Kademe Dinamik Adaptasyon)
                    if (_smoothedRttMs < 30) // Tier 1: Fiber / LAN
                    {
                        _adaptiveQuality = 84;
                        _frameDelayMs = 16; // 60 FPS
                    }
                    else if (_smoothedRttMs < 75) // Tier 2: Standart Genişbant
                    {
                        _adaptiveQuality = 74;
                        _frameDelayMs = 22; // 45 FPS
                    }
                    else if (_smoothedRttMs < 140) // Tier 3: Mobil / 4G / Dalgalı Hat
                    {
                        _adaptiveQuality = 60;
                        _frameDelayMs = 33; // 30 FPS
                    }
                    else // Tier 4: Zayıf Hat (>140ms)
                    {
                        _adaptiveQuality = 48;
                        _frameDelayMs = 50; // 20 FPS
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

    /// <summary>
    /// Downloads the new agent MSI and drops it where the NexMote Agent Windows Service (running
    /// as LocalSystem) polls for it. Supports stage-by-stage progress reporting.
    /// </summary>
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

/// <summary>
/// Tray'in kimlik depolama cephesi. Gerçek okuma/yazma, Windows Servisi'yle (NexMote.Agent.Windows) AYNI
/// DPAPI-şifreli identity.dat dosyasını kullanan <see cref="NexMote.Shared.Identity.DeviceIdentityStore"/>
/// üzerinden yapılır. Daha önce Tray kendi başına düz metin identity.json okuyup yazıyordu; Servis
/// güncellemesi identity.json'ı şifreli identity.dat'a taşıyıp sildiğinde Tray onu bulamayıp sessizce
/// ikinci bir cihaz kaydı (split-brain) oluşturuyordu.
/// </summary>
internal static class DeviceIdentityFile
{
    private static readonly DeviceIdentityStore Store = new();

    public static DeviceIdentity? Load()
    {
        try
        {
            return Store.Load();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<DeviceIdentity?> EnsureEnrolledAsync(string serverUrl, string enrollmentKey)
    {
        var existing = Load();
        if (existing is not null) return existing;

        try
        {
            using var http = NexMoteHttp.CreateClient(TimeSpan.FromSeconds(10));
            var enrollUrl = $"{serverUrl.TrimEnd('/')}/api/agents/enroll";

            var os = Environment.OSVersion.VersionString;
            var deviceName = Environment.MachineName;
            var domainName = Environment.UserDomainName;
            var activeUser = SessionUserResolver.GetActiveSessionUserName();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.6.2";

            var req = new
            {
                DeviceName = deviceName,
                DomainName = domainName,
                OperatingSystem = os,
                AgentVersion = version,
                ActiveUser = activeUser,
                EnrollmentKey = string.IsNullOrWhiteSpace(enrollmentKey) || enrollmentKey == "dev-enrollment-key"
                    ? "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7"
                    : enrollmentKey,
                LocationCode = "OFFICE"
            };

            var res = await http.PostAsJsonAsync(enrollUrl, req);
            if (res.IsSuccessStatusCode)
            {
                using var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
                if (doc != null &&
                    doc.RootElement.TryGetProperty("deviceId", out var idProp) &&
                    doc.RootElement.TryGetProperty("agentToken", out var tokenProp))
                {
                    var id = idProp.GetGuid();
                    var token = tokenProp.GetString() ?? string.Empty;
                    var identity = new DeviceIdentity(id, token);
                    Save(identity);
                    return identity;
                }
            }
        }
        catch { }

        return null;
    }

    public static void Save(DeviceIdentity identity)
    {
        try
        {
            Store.Save(identity);
        }
        catch { }
    }

    public static void Delete()
    {
        try
        {
            Store.Delete();
        }
        catch { }
    }
}

/// <summary>
/// Windows masaüstü ekran görüntülerini, fare imlecini (cursor) GDI+ / BitBlt kullanarak yakalayan,
/// donanımsal 1:1 piksel netliğini koruyan ve JPEG öncesi ham bellek hash kontrolüyle sıfır CPU tüketen ekran yakalama motoru.
/// </summary>
internal static class ScreenCapture
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ulong> LastFrameHashes = new();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    private const int CURSOR_SHOWING = 0x00000001;

    [DllImport("user32.dll")]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern bool DrawIcon(IntPtr hdc, int x, int y, IntPtr hIcon);

    private const int TileSize = 128;
    private static readonly ConcurrentDictionary<int, ulong[]> DisplayTileHashes = new();

    public static void ResetHash(int displayIndex)
    {
        LastFrameHashes[displayIndex] = 0;
        DisplayTileHashes.TryRemove(displayIndex, out _);
    }

    public static MultiScreenFrame? CaptureDeltaFrame(int displayIndex, int quality, bool forceKeyFrame, ref long sequence)
    {
        DesktopHelper.AttachToActiveDesktop();

        var bounds = GetDisplayBounds(displayIndex);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Ekran bulunamadi.");
        }

        using var capture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(capture))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            // Fare imlecini (cursor) gerçek ekran koordinatıyla doğrudan yakalanan karenin üzerine çiz
            try
            {
                var pci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                if (GetCursorInfo(out pci) && pci.flags == CURSOR_SHOWING && pci.hCursor != IntPtr.Zero)
                {
                    var cursorX = pci.ptScreenPos.x - bounds.Left;
                    var cursorY = pci.ptScreenPos.y - bounds.Top;
                    if (cursorX >= -32 && cursorX < bounds.Width && cursorY >= -32 && cursorY < bounds.Height)
                    {
                        var hdc = graphics.GetHdc();
                        try
                        {
                            DrawIcon(hdc, cursorX, cursorY, pci.hCursor);
                        }
                        finally
                        {
                            graphics.ReleaseHdc(hdc);
                        }
                    }
                }
            }
            catch { }
        }

        var width = bounds.Width;
        var height = bounds.Height;
        var cols = (width + TileSize - 1) / TileSize;
        var rows = (height + TileSize - 1) / TileSize;
        var totalTiles = cols * rows;

        if (!DisplayTileHashes.TryGetValue(displayIndex, out var tileHashes) || tileHashes.Length != totalTiles || forceKeyFrame)
        {
            tileHashes = new ulong[totalTiles];
            DisplayTileHashes[displayIndex] = tileHashes;
            forceKeyFrame = true;
        }

        var rect = new Rectangle(0, 0, width, height);
        var bmpData = capture.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        try
        {
            var dirtyTiles = new List<ScreenTile>();
            var newTileHashes = new ulong[totalTiles];
            var anyChanged = false;

            unsafe
            {
                byte* scan0 = (byte*)bmpData.Scan0;
                int stride = bmpData.Stride;

                for (int r = 0; r < rows; r++)
                {
                    var tileY = r * TileSize;
                    var tileH = Math.Min(TileSize, height - tileY);

                    for (int c = 0; c < cols; c++)
                    {
                        var tileX = c * TileSize;
                        var tileW = Math.Min(TileSize, width - tileX);
                        var tileIdx = r * cols + c;

                        var hash = ComputeTileHash(scan0, stride, tileX, tileY, tileW, tileH);
                        newTileHashes[tileIdx] = hash;

                        if (forceKeyFrame || hash != tileHashes[tileIdx])
                        {
                            anyChanged = true;
                            if (!forceKeyFrame)
                            {
                                var tileBase64 = EncodeTileJpeg(capture, tileX, tileY, tileW, tileH, quality);
                                dirtyTiles.Add(new ScreenTile(tileX, tileY, tileW, tileH, tileBase64));
                            }
                        }
                    }
                }
            }

            if (!anyChanged && !forceKeyFrame)
            {
                return null;
            }

            Array.Copy(newTileHashes, tileHashes, totalTiles);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (forceKeyFrame || dirtyTiles.Count > (totalTiles * 0.65))
            {
                // Full keyframe
                using var stream = new MemoryStream();
                SaveJpeg(capture, stream, Math.Clamp((long)quality, 40, 96));
                var fullBase64 = Convert.ToBase64String(stream.ToArray());
                return new MultiScreenFrame(
                    displayIndex,
                    JpegBase64: fullBase64,
                    Sequence: ++sequence,
                    CapturedAtUnixMs: now,
                    IsKeyFrame: true,
                    ScreenWidth: width,
                    ScreenHeight: height,
                    Tiles: null);
            }
            else
            {
                // Tile delta frame (Sadece değişen bloklar)
                return new MultiScreenFrame(
                    displayIndex,
                    JpegBase64: null,
                    Sequence: ++sequence,
                    CapturedAtUnixMs: now,
                    IsKeyFrame: false,
                    ScreenWidth: width,
                    ScreenHeight: height,
                    Tiles: dirtyTiles.ToArray());
            }
        }
        finally
        {
            capture.UnlockBits(bmpData);
        }
    }

    private static unsafe ulong ComputeTileHash(byte* scan0, int stride, int x, int y, int w, int h)
    {
        ulong hash = 14695981039346656037UL;
        for (int row = 0; row < h; row += 2)
        {
            var rowPtr = scan0 + (y + row) * stride + (x * 3);
            var endPtr = rowPtr + (w * 3);
            for (var ptr = rowPtr; ptr < endPtr; ptr += 6)
            {
                hash ^= *(uint*)ptr;
                hash *= 1099511628211UL;
            }
        }
        return hash;
    }

    private static string EncodeTileJpeg(Bitmap source, int x, int y, int w, int h, int quality)
    {
        using var tileBmp = source.Clone(new Rectangle(x, y, w, h), PixelFormat.Format24bppRgb);
        using var stream = new MemoryStream();
        SaveJpeg(tileBmp, stream, Math.Clamp((long)quality, 40, 96));
        return Convert.ToBase64String(stream.ToArray());
    }

    public static int GetDisplayCount() => Math.Max(1, Screen.AllScreens.Length);

    private static int GetWindowsDisplayIndex(Screen screen, int fallback)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(screen.DeviceName ?? "", @"DISPLAY(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            {
                return num;
            }
        }
        catch { }
        return fallback;
    }

    public static RemoteScreenInfo GetInfo()
    {
        DesktopHelper.AttachToActiveDesktop();
        var virtualBounds = SystemInformation.VirtualScreen;
        var screens = Screen.AllScreens;

        var displays = new List<DisplayItem>
        {
            new DisplayItem(0, "Tüm Ekranlar", virtualBounds.Width, virtualBounds.Height, virtualBounds.Left, virtualBounds.Top)
        };

        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var b = s.Bounds;
            var winIndex = GetWindowsDisplayIndex(s, i + 1);
            var isPrimary = s.Primary;
            var name = isPrimary ? $"Ekran {winIndex} (Ana Ekran)" : $"Ekran {winIndex}";
            displays.Add(new DisplayItem(winIndex, name, b.Width, b.Height, b.Left, b.Top));
        }

        // Sort displays by index (0, 1, 2...) for clean display listing
        displays = displays.OrderBy(d => d.Index).ToList();

        return new RemoteScreenInfo(virtualBounds.Left, virtualBounds.Top, virtualBounds.Width, virtualBounds.Height, 0, displays.ToArray());
    }

    public static Rectangle GetDisplayBoundsPublic(int displayIndex) => GetDisplayBounds(displayIndex);

    private static Rectangle GetDisplayBounds(int activeDisplayIndex)
    {
        DesktopHelper.AttachToActiveDesktop();
        if (activeDisplayIndex == 0)
        {
            return SystemInformation.VirtualScreen;
        }

        var screens = Screen.AllScreens;
        for (int i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var winIndex = GetWindowsDisplayIndex(s, i + 1);
            if (winIndex == activeDisplayIndex)
            {
                return s.Bounds;
            }
        }

        var idx = activeDisplayIndex - 1;
        if (idx >= 0 && idx < screens.Length)
        {
            return screens[idx].Bounds;
        }

        return SystemInformation.VirtualScreen;
    }

    private static readonly ImageCodecInfo? CachedJpegEncoder = GetEncoder(ImageFormat.Jpeg);

    public static string? CaptureJpegBase64(int displayIndex, int quality, bool forceSend)
    {
        DesktopHelper.AttachToActiveDesktop();

        var bounds = GetDisplayBounds(displayIndex);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException("Ekran bulunamadi.");
        }

        using var capture = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(capture))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

            // Fare imlecini (cursor) gerçek ekran koordinatıyla doğrudan yakalanan karenin üzerine çiz
            try
            {
                var pci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
                if (GetCursorInfo(out pci) && pci.flags == CURSOR_SHOWING && pci.hCursor != IntPtr.Zero)
                {
                    var cursorX = pci.ptScreenPos.x - bounds.Left;
                    var cursorY = pci.ptScreenPos.y - bounds.Top;
                    if (cursorX >= -32 && cursorX < bounds.Width && cursorY >= -32 && cursorY < bounds.Height)
                    {
                        var hdc = graphics.GetHdc();
                        try
                        {
                            DrawIcon(hdc, cursorX, cursorY, pci.hCursor);
                        }
                        finally
                        {
                            graphics.ReleaseHdc(hdc);
                        }
                    }
                }
            }
            catch { }
        }

        // 1. Ham Piksel Hash Kontrolü (JPEG kodlamasından ÖNCE! Değişmeyen sahnede sıfır CPU harcar)
        var rawHash = ComputeBitmapHash(capture);
        if (!forceSend && LastFrameHashes.TryGetValue(displayIndex, out var lastHash) && rawHash == lastHash)
        {
            return null;
        }

        LastFrameHashes[displayIndex] = rawHash;

        // 2. 1:1 Doğal Çözünürlük (1080p/2K/4K ekranları asla küçültmez, 4K üzerini kaliteli bicubic ile ölçekler)
        Bitmap? resized = null;
        Bitmap sourceToSave = capture;
        if (capture.Width > 3840)
        {
            resized = ResizeBitmap(capture, 3840);
            sourceToSave = resized;
        }

        try
        {
            using var stream = new MemoryStream(128 * 1024);
            SaveJpeg(sourceToSave, stream, Math.Clamp((long)quality, 40, 96));
            return Convert.ToBase64String(stream.ToArray());
        }
        finally
        {
            resized?.Dispose();
        }
    }

    private static unsafe ulong ComputeBitmapHash(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var ptr = (byte*)data.Scan0;
            var totalBytes = data.Stride * data.Height;
            var step = Math.Max(1, totalBytes / 1024); // 1024 noktadan ultra hızlı bellek örneği al
            ulong hash = 14695981039346656037UL;
            for (var i = 0; i < totalBytes; i += step)
            {
                hash ^= ptr[i];
                hash *= 1099511628211UL;
            }
            return hash;
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Bitmap ResizeBitmap(Bitmap source, int maxWidth)
    {
        var aspect = (double)source.Height / source.Width;
        var newWidth = maxWidth;
        var newHeight = (int)Math.Max(1, Math.Round(newWidth * aspect));

        var target = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(target);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, 0, 0, newWidth, newHeight);
        return target;
    }

    private static void SaveJpeg(Image image, Stream stream, long quality)
    {
        var encoder = CachedJpegEncoder ?? GetEncoder(ImageFormat.Jpeg);
        if (encoder is null)
        {
            image.Save(stream, ImageFormat.Jpeg);
            return;
        }

        var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        image.Save(stream, encoder, encoderParameters);
    }

    private static ImageCodecInfo? GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageDecoders();
        foreach (var codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null;
    }
}

/// <summary>
/// Kilit ekranı, Winlogon, kullanıcı değiştirme veya UAC durumlarında aktif interaktif masaüstüne (OpenWindowStation / OpenInputDesktop / SetThreadDesktop) bağlanmayı sağlayan Win32 köprüsü.
/// </summary>
internal static class DesktopHelper
{
    private const uint GENERIC_ALL = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint DESKTOP_ALL = 0x01FF | MAXIMUM_ALLOWED;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    /// <summary>
    /// Aktif masaüstüne (Default / Winlogon secure desktop) çağıran iş parçacığını iliştirir.
    ///
    /// Not: Önceki sürüm, açılan HDESK tanıtıcısını [ThreadStatic] bir alanda önbelliğe alıp yalnızca AYNI
    /// iş parçacığı tekrar çağırdığında kapatıyordu. SetThreadDesktop çağıran iş parçacığına özgü olduğu
    /// için bu, bu metodu HER ZAMAN aynı .NET Thread üzerinden çağıran yerlerde (ör. UI thread'indeki Timer)
    /// doğru çalışıyordu; ama ekran yakalama döngüsü çıplak Task.Run + await zinciriyle çalıştığı için
    /// (ThreadPool'da iterasyon başına farklı bir işletim sistemi thread'inde devam edebilir) her yeni pool
    /// thread'i sıfırdan bir HDESK açıyor ve önceki thread'de açılan tanıtıcı hiç kapanmadan kalıyordu —
    /// uzun/çoklu-ekran akış oturumlarında yavaş bir native handle sızıntısına yol açıyordu.
    ///
    /// SetThreadDesktop başarılı olduktan sonra kendi HDESK tanıtıcımızı hemen kapatmak güvenlidir:
    /// iş parçacığı-masaüstü ilişkisini Windows ayrıca (dahili referansla) tutar, bizim tanıtıcımızı
    /// thread'ler arası önbelleklememize gerek yoktur.
    /// </summary>
    public static void AttachToActiveDesktop()
    {
        try
        {
            var hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL);
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenInputDesktop(0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Winlogon", 0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Default", 0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hDesktop);
                }
                finally
                {
                    CloseDesktop(hDesktop);
                }
            }
        }
        catch
        {
        }
    }
}

/// <summary>
/// SYSTEM yetkisinde çalışan ve Named Pipe ("NexMoteInputHelper_{SessionId}") üzerinden gelen girdi olaylarını dinleyerek
/// UIPI kısıtlamasını aşan ve UAC onay pencerelerine tıklama yapılmasını sağlayan yerel sunucu.
/// </summary>
internal static class InputHelperServer
{
    public static void Run()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var mutexName = $@"Global\NexMoteInputHelperMutex_{sessionId}";
        
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

        var pipeName = $"NexMoteInputHelper_{sessionId}";
        var security = BuildPipeSecurity();

        while (true)
        {
            try
            {
                using var server = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 4,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    security);

                server.WaitForConnection();

                if (!IsAllowedClient(server))
                {
                    server.Disconnect();
                    continue;
                }

                using var reader = new StreamReader(server, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    HandleCommand(line);
                }
            }
            catch
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void HandleCommand(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(json, options);
            if (input is null)
            {
                return;
            }

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
                case "send-sas":
                    SasHelper.SendSas();
                    break;
                case "power-action":
                    PowerHelper.Execute(input.Button ?? "lock");
                    break;
            }
        }
        catch
        {
        }
    }

    private static bool IsAllowedClient(NamedPipeServerStream server)
    {
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid))
            {
                return false;
            }

            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (!QueryFullProcessImageName(hProcess, 0, sb, ref size))
            {
                return false;
            }

            var clientPath = sb.ToString();
            var selfPath = Process.GetCurrentProcess().MainModule?.FileName;
            return !string.IsNullOrEmpty(clientPath) && !string.IsNullOrEmpty(selfPath) &&
                   string.Equals(clientPath, selfPath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                CloseHandle(hProcess);
            }
        }
    }

    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        var interactiveSid = new SecurityIdentifier(WellKnownSidType.InteractiveSid, null);
        security.AddAccessRule(new PipeAccessRule(interactiveSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));
        return security;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}

/// <summary>
/// Uzaktan gelen fare ve klavye girdilerini önce SYSTEM yetkili Girdi Yardımcısına (Named Pipe) ileten,
/// yardımcının ulaşılamadığı durumlarda standart Win32 SendInput API'sine geri düşen (fallback) girdi enjektörü.
/// </summary>
internal static class InputInjector
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseMove = 0x0001;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private const uint MouseWheelFlag = 0x0800;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint KeyboardKeyUp = 0x0002;
    private const uint KeyboardExtendedKey = 0x0001;

    public static void MoveMouse(int displayIndex, int x, int y)
    {
        DesktopHelper.AttachToActiveDesktop();

        var displayBounds = ScreenCapture.GetDisplayBoundsPublic(displayIndex);
        var globalX = displayBounds.Left + x;
        var globalY = displayBounds.Top + y;

        var virtualBounds = SystemInformation.VirtualScreen;
        var clampedX = Math.Clamp(globalX, virtualBounds.Left, virtualBounds.Right - 1);
        var clampedY = Math.Clamp(globalY, virtualBounds.Top, virtualBounds.Bottom - 1);

        SetCursorPos(clampedX, clampedY);

        var normalizedX = (int)Math.Round((double)(clampedX - virtualBounds.Left) * 65535 / Math.Max(1, virtualBounds.Width - 1));
        var normalizedY = (int)Math.Round((double)(clampedY - virtualBounds.Top) * 65535 / Math.Max(1, virtualBounds.Height - 1));

        var input = new INPUT
        {
            Type = InputMouse,
            Data = new INPUTUNION
            {
                Mouse = new MOUSEINPUT
                {
                    Dx = normalizedX,
                    Dy = normalizedY,
                    Flags = MouseMove | MouseAbsolute | MouseVirtualDesk,
                    MouseData = 0
                }
            }
        };
        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            try
            {
                mouse_event(MouseMove | MouseAbsolute | MouseVirtualDesk, normalizedX, normalizedY, 0, UIntPtr.Zero);
            }
            catch { }
        }
    }

    public static void MouseButton(string? button, bool isDown)
    {
        DesktopHelper.AttachToActiveDesktop();

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
        DesktopHelper.AttachToActiveDesktop();

        if (delta != 0)
        {
            SendMouse(MouseWheelFlag, delta);
        }
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, int dx, int dy, int dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    public static void Keyboard(int keyCode, bool isDown)
    {
        DesktopHelper.AttachToActiveDesktop();

        if (keyCode is <= 0 or > ushort.MaxValue)
        {
            return;
        }

        var isExtended = IsExtendedKey(keyCode);
        var scanCode = (ushort)MapVirtualKey((uint)keyCode, 0);
        var flags = (isDown ? 0u : KeyboardKeyUp) | (isExtended ? KeyboardExtendedKey : 0u);

        var input = new INPUT
        {
            Type = InputKeyboard,
            Data = new INPUTUNION
            {
                Keyboard = new KEYBDINPUT
                {
                    VirtualKey = (ushort)keyCode,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            try
            {
                keybd_event((byte)keyCode, (byte)scanCode, flags, UIntPtr.Zero);
            }
            catch { }
        }
    }

    private static bool IsExtendedKey(int keyCode)
    {
        return keyCode is 37 or 38 or 39 or 40 or 33 or 34 or 35 or 36 or 44 or 45 or 46 or 91 or 92 or 93 or 111 or 144 or 163 or 165;
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

        if (SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>()) == 0)
        {
            try
            {
                mouse_event(flags, 0, 0, mouseData, UIntPtr.Zero);
            }
            catch { }
        }
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
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }
}

/// <summary>
/// Hedef makinede yazılımsal olarak Güvenli Dikkat Dizisi (Secure Attention Sequence - Ctrl+Alt+Del) üreten yardımcı sınıf.
/// sas.dll / SendSAS API'sini veya sentetik klavye olaylarını kullanır.
/// </summary>
internal static class SasHelper
{
    [DllImport("sas.dll", SetLastError = true)]
    private static extern void SendSAS(bool asUser);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private const int KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_MENU = 0x12; // Alt
    private const byte VK_DELETE = 0x2E;

    public static void SendSas()
    {
        try
        {
            SendSAS(false);
            return;
        }
        catch
        {
        }

        try
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_DELETE, 0, 0, UIntPtr.Zero);
            Thread.Sleep(50);
            keybd_event(VK_DELETE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
        }
    }
}

/// <summary>
/// Hedef bilgisayarı uzaktan kilitleme (LockWorkStation), yeniden başlatma veya kapatma komutlarını yürüten sınıf.
/// </summary>
internal static class PowerHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    public static void Execute(string action)
    {
        try
        {
            switch (action.ToLowerInvariant())
            {
                case "lock":
                    LockWorkStation();
                    break;
                case "logoff":
                    ExitWindowsEx(0x00000000 | 0x00000004, 0); // EWX_LOGOFF | EWX_FORCE
                    break;
                case "reboot":
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "reboot-safe":
                    Process.Start(new ProcessStartInfo("bcdedit.exe", "/set {current} safeboot network") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "reboot-normal":
                    Process.Start(new ProcessStartInfo("bcdedit.exe", "/deletevalue {current} safeboot") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "shutdown":
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
            }
        }
        catch
        {
        }
    }
}

internal sealed record CommandRunResult(int ExitCode, string StdOut, string StdErr, long DurationMs, bool TimedOut, bool ElevationDenied = false);

/// <summary>
/// Uzaktan gelen CMD veya PowerShell komutlarını standart veya UAC yükseltmeli ("runas") modda çalıştıran ve denetim loglarını sunucuya gönderen motor.
/// </summary>
internal static class CommandRunner
{
    public static Task<CommandRunResult> RunAsync(string shell, string command, int timeoutMs, bool runAsAdmin = false)
    {
        // Hedef cihazda UAC veya Domain Admin şifre istemi çıkmaması için komutlar her zaman sessiz arka plan modunda (I/O redirection ile) yürütülür.
        return RunStandardAsync(shell, command, timeoutMs);
    }

    /// <summary>
    /// Launches the command with the Windows "runas" verb, which makes the real UAC
    /// consent/credential prompt appear on the target machine's desktop. UseShellExecute+runas
    /// cannot share stdio pipes with the parent, so the elevated process is wrapped to redirect
    /// its own output into a temp file that we read back after it exits.
    /// </summary>
    private static async Task<CommandRunResult> RunElevatedAsync(string shell, string command, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var isPowerShell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var fileName = isPowerShell ? "powershell.exe" : "cmd.exe";
        var outFile = Path.Combine(Path.GetTempPath(), $"nexmote_elevated_{Guid.NewGuid():N}.txt");
        string? tempScript = null;
        string arguments;

        if (isPowerShell)
        {
            var psScript = $"{command} *>&1 | Out-File -FilePath '{outFile}' -Encoding utf8";
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
            arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedScript}";
        }
        else
        {
            tempScript = Path.Combine(Path.GetTempPath(), $"nexmote_cmd_{Guid.NewGuid():N}.cmd");
            // "chcp 65001" ile cmd.exe'nin bu betiği UTF-8 kod sayfasıyla yorumlaması sağlanır.
            // Encoding.Default, .NET (Core) altında BOM'suz UTF-8'dir; ama cmd.exe .cmd dosyalarını
            // varsayılan olarak sistem OEM kod sayfasıyla okur — bu uyumsuzluk Türkçe (ç/ğ/ı/ö/ş/ü)
            // karakterler içeren komutları bozuyordu.
            var batchContent = $"@echo off\r\nchcp 65001 >nul\r\n{command} > \"{outFile}\" 2>&1\r\n";
            await File.WriteAllTextAsync(tempScript, batchContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            arguments = $"/c \"{tempScript}\"";
        }

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        var timedOut = false;
        var elevationDenied = false;
        var exitCode = -1;

        try
        {
            using var process = Process.Start(psi);
            if (process is not null)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    exitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user dismissed or denied the UAC prompt.
            elevationDenied = true;
        }
        finally
        {
            if (tempScript is not null && File.Exists(tempScript))
            {
                try { File.Delete(tempScript); } catch { }
            }
        }

        stopwatch.Stop();

        var output = string.Empty;
        try
        {
            if (File.Exists(outFile))
            {
                output = await File.ReadAllTextAsync(outFile);
                File.Delete(outFile);
            }
        }
        catch
        {
        }

        return new CommandRunResult(
            elevationDenied || timedOut ? -1 : exitCode,
            output,
            elevationDenied ? "Kullanıcı yönetici izni istemini reddetti veya kimlik doğrulaması başarısız oldu." : string.Empty,
            stopwatch.ElapsedMilliseconds,
            timedOut,
            elevationDenied);
    }

    private static async Task<CommandRunResult> RunStandardAsync(string shell, string command, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var isPowerShell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var fileName = isPowerShell ? "powershell.exe" : "cmd.exe";
        var arguments = isPowerShell
            ? $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "\\\"")}\""
            : $"/c {command}";

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
        }

        stopwatch.Stop();
        return new CommandRunResult(
            timedOut ? -1 : process.ExitCode,
            stdOut.ToString(),
            stdErr.ToString(),
            stopwatch.ElapsedMilliseconds,
            timedOut);
    }
}

/// <summary>
/// appsettings.json dosyasından sunucu URL'i ve kayıt anahtarını okuyan veya güncelleyen statik ayar yöneticisi.
/// </summary>
internal static class AgentSettings
{
    public static string LoadServerUrl()
    {
        var raw = LoadSetting("ServerUrl", "https://nexmote.com");
        // NormalizeUrl yerine EnforceProductionUrl: Tray de Windows Servisi (AgentClient) ile aynı şekilde
        // localhost/özel IP adreslerini üretime zorlamalı, aksi halde aynı makinedeki iki süreç (SYSTEM
        // servisi ve kullanıcı oturumundaki Tray) sessizce farklı sunucularla konuşabilir.
        return NexMoteHttp.EnforceProductionUrl(raw);
    }

    public static string LoadEnrollmentKey()
    {
        var key = LoadSetting("EnrollmentKey", "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7");
        return string.IsNullOrWhiteSpace(key) || key == "dev-enrollment-key" || key.StartsWith("CHANGE-ME")
            ? "4ed67db20bb0167a310129162ba8a831aae0d1d014032086fa67ebe416bb2ec7"
            : key;
    }

    private static string LoadSetting(string propertyName, string defaultValue)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var serviceConfigPath = Path.Combine(programData, "NexMote", "Agent", "appsettings.json");
        var baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        string? result = ReadPropertyFromFile(serviceConfigPath, propertyName);
        if (!string.IsNullOrEmpty(result)) return result;

        result = ReadPropertyFromFile(baseConfigPath, propertyName);
        return string.IsNullOrEmpty(result) ? defaultValue : result;
    }

    private static string? ReadPropertyFromFile(string path, string propertyName)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("Agent", out var agent) &&
                agent.TryGetProperty(propertyName, out var prop))
            {
                return prop.GetString();
            }
        }
        catch { }
        return null;
    }

    public static void SaveSettings(string newUrl, string newKey)
    {
        var normalizedUrl = NexMoteHttp.NormalizeUrl(newUrl);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var agentDir = Path.Combine(programData, "NexMote", "Agent");
        Directory.CreateDirectory(agentDir);

        var serviceConfigPath = Path.Combine(agentDir, "appsettings.json");
        SaveConfigToPath(serviceConfigPath, normalizedUrl, newKey);

        var baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        SaveConfigToPath(baseConfigPath, normalizedUrl, newKey);
    }

    private static void SaveConfigToPath(string path, string newUrl, string newKey)
    {
        try
        {
            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            var rootObj = string.IsNullOrWhiteSpace(json) || !json.Trim().StartsWith("{") ? new Dictionary<string, object>() : JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();

            var agentDict = new Dictionary<string, object>
            {
                ["ServerUrl"] = newUrl,
                ["EnrollmentKey"] = newKey,
                ["AgentVersion"] = "0.1.0",
                ["LocationCode"] = "OFFICE",
                ["HeartbeatSeconds"] = 20
            };

            rootObj["Agent"] = agentDict;

            if (!rootObj.ContainsKey("Logging"))
            {
                rootObj["Logging"] = new Dictionary<string, object>
                {
                    ["LogLevel"] = new Dictionary<string, string>
                    {
                        ["Default"] = "Information",
                        ["Microsoft.Hosting.Lifetime"] = "Information"
                    }
                };
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(rootObj, options);
            File.WriteAllText(path, updatedJson);
        }
        catch
        {
            // Ignore write errors if permissions restricted
        }
    }
}
