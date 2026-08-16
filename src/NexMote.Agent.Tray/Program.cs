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

namespace NexMote.Agent.Tray;

/// <summary>
/// Windows Sistem Tepsisi (Tray) uygulamasının ana başlangıç sınıfı.
/// DPI farkındalığını yapılandırır ve komut satırı parametresine göre normal Tepsi GUI veya SYSTEM yetkili Girdi Yardımcısı (--input-helper) modunda çalışır.
/// </summary>
internal static class Program
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

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

        // SYSTEM yetkisinde çalışan Girdi Yardımcısı modu kontrolü (UAC tıklamaları için)
        if (args.Length > 0 && string.Equals(args[0], "--input-helper", StringComparison.OrdinalIgnoreCase))
        {
            InputHelperServer.Run();
            return;
        }

        // Tekil Oturum Mutex Kontrolü (Her kullanıcı oturumunda en fazla 1 adet Agent Tray çalışabilir)
        var sessionId = Process.GetCurrentProcess().SessionId;
        var mutexName = $@"Global\NexMote_Agent_Tray_SingleInstance_Session_{sessionId}";
        using var mutex = new Mutex(true, mutexName, out var createdNew);
        if (!createdNew)
        {
            // Bu oturumda zaten çalışan bir Agent Tray mevcut, ikinci kopya açılmadan sessizce sonlandırılır.
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
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
    private readonly ToolStripMenuItem _screenItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _signalingTimer;
    private readonly SynchronizationContext? _uiContext;
    private readonly RemoteScreenStreamer _streamer;
    private DashboardForm? _dashboardForm;
    private string _serverUrl;

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
        menu.Items.Add("🛡️ Durum Panelini Aç", null, (_, _) => ShowDashboard());
        menu.Items.Add("🚀 Güncelleme Kontrol Et", null, async (_, _) => await CheckForAgentUpdatesAsync(isManual: true));
        menu.Items.Add("Paneli Ac", null, (_, _) => OpenWebPanel());
        menu.Items.Add("Sunucu Ayarları...", null, (_, _) => ShowServerSettingsDialog());
        menu.Items.Add("Durumu Yenile", null, (_, _) => RefreshStatus(showBalloon: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Tray'i Kapat", null, (_, _) => ExitThread());

        try
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = IconHelper.GetAppIcon(),
                Text = "NexMote Agent",
                ContextMenuStrip = menu,
                Visible = true
            };
            _notifyIcon.DoubleClick += (_, _) => ShowDashboard();
        }
        catch
        {
            // Running in session without interactive tray (e.g. Lock screen / Winlogon)
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

        RefreshStatus(showBalloon: false);
        _ = _streamer.EnsureStartedAsync();

        // Açılıştan 4 saniye sonra sessizce arka planda sunucu sürüm kontrolü yap
        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            await CheckForAgentUpdatesAsync(isManual: false);
        });
    }

    private async Task CheckForAgentUpdatesAsync(bool isManual)
    {
        try
        {
            var checkUrl = $"{_serverUrl.TrimEnd('/')}/api/updates/check";
            using var http = new HttpClient();
            var json = await http.GetStringAsync(checkUrl);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("agent", out var agent) &&
                agent.TryGetProperty("version", out var verProp) &&
                agent.TryGetProperty("downloadUrl", out var urlProp))
            {
                var latestVersion = verProp.GetString();
                var downloadUrl = urlProp.GetString();
                var releaseNotes = agent.TryGetProperty("releaseNotes", out var notesProp) ? notesProp.GetString() : "Performans ve kararlılık iyileştirmesi.";

                var runningVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.5.4";
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
                    if (_notifyIcon != null)
                    {
                        _notifyIcon.BalloonTipTitle = "NexMote Ajan Güncellemesi";
                        _notifyIcon.BalloonTipText = "Güncelleme paketi indiriliyor...";
                        _notifyIcon.ShowBalloonTip(2000);
                    }

                    await RemoteScreenStreamer.PerformSelfUpdateAsync(downloadUrl);

                    if (_notifyIcon != null)
                    {
                        _notifyIcon.BalloonTipTitle = "NexMote Ajan Güncellemesi";
                        _notifyIcon.BalloonTipText = "Güncelleme indirildi. Servis tarafından arka planda sessizce kurulacak.";
                        _notifyIcon.ShowBalloonTip(3000);
                    }

                    if (isManual)
                    {
                        MessageBox.Show(
                            "Güncelleme paketi başarıyla indirildi.\n\nNexMote Servisi birkaç saniye içinde güncellemeyi arka planda tamamlayacaktır.",
                            "Güncelleme İndirildi",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
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
        var status = GetServiceStatus();
        _statusItem.Text = $"Servis durumu: {status}";
        if (_notifyIcon != null)
        {
            try
            {
                _notifyIcon.Text = $"NexMote Agent - {status}";
                if (showBalloon)
                {
                    _notifyIcon.BalloonTipTitle = "NexMote Agent";
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
            _dashboardForm.RefreshState();
            _dashboardForm.Show();
            _dashboardForm.BringToFront();
            _dashboardForm.Activate();
            return;
        }

        _dashboardForm = new DashboardForm(
            getServiceStatus: GetServiceStatus,
            getServerUrl: () => _serverUrl,
            getScreenStatus: () => (_screenItem.Text ?? string.Empty).Replace("Goruntu akisi: ", string.Empty),
            getIsConnected: () => _streamer.IsConnected,
            openPanel: OpenWebPanel,
            openSettings: ShowServerSettingsDialog,
            refresh: () => RefreshStatus(showBalloon: false),
            checkUpdates: () => _ = CheckForAgentUpdatesAsync(isManual: true),
            saveSettings: (newUrl, newKey) =>
            {
                _serverUrl = newUrl;
                AgentSettings.SaveSettings(newUrl, newKey);
                _serverItem.Text = $"Sunucu: {newUrl}";
                _streamer.UpdateServerUrl(newUrl);
            });
        _dashboardForm.Show();
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

        var lblUrl = new Label { Left = 20, Top = 15, Width = 400, Text = "Sunucu Adresi (Server URL):", ForeColor = Color.FromArgb(0x64, 0x74, 0x8B) };
        var txtUrl = new TextBox { Left = 20, Top = 38, Width = 400, Text = _serverUrl, BorderStyle = BorderStyle.FixedSingle };

        var lblKey = new Label { Left = 20, Top = 80, Width = 400, Text = "Kayıt Anahtarı (Enrollment Key):", ForeColor = Color.FromArgb(0x64, 0x74, 0x8B) };
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
            var newUrl = txtUrl.Text.Trim().TrimEnd('/');
            var newKey = txtKey.Text.Trim();

            if (Uri.TryCreate(newUrl, UriKind.Absolute, out var parsedUri))
            {
                _serverUrl = newUrl;
                AgentSettings.SaveSettings(newUrl, newKey);
                _serverItem.Text = $"Sunucu: {newUrl}";
                _streamer.UpdateServerUrl(newUrl);
                MessageBox.Show($"Sunucu ayarları güncellendi:\nURL: {newUrl}\n\nYeni sunucuya bağlanılıyor...", "NexMote Agent", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Geçersiz sunucu adresi URL formatı.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    protected override void ExitThreadCore()
    {
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
    private readonly Action<string, string> _saveSettings;

    // UI Controls
    private Panel _alertBanner = null!;
    private Label _alertTitle = null!;
    private Label _alertSubtitle = null!;
    private Button _alertActionBtn = null!;

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
        _saveSettings = saveSettings;

        Text = "NexMote";
        ClientSize = new Size(880, 620);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);
        Icon = IconHelper.GetAppIcon();

        BuildLayout();
        RefreshState();
    }

    private void BuildLayout()
    {
        Controls.Clear();

        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.5.4";

        // 1. TOP HEADER BAR
        var headerPanel = new Panel { Left = 0, Top = 0, Width = 880, Height = 56, BackColor = Color.White };
        
        var logoIcon = new Label
        {
            Text = "N",
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            Left = 24,
            Top = 13,
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
            Text = "NexMote",
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 64,
            Top = 14,
            AutoSize = true
        };

        var versionLabel = new Label
        {
            Text = $"v{versionStr} • Ajan v{versionStr}",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = TextMuted,
            Left = 470,
            Top = 20,
            AutoSize = true
        };

        var langTr = new Label
        {
            Text = "TR",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0x02, 0x84, 0xC7),
            Left = 640,
            Top = 16,
            Width = 32,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };

        var langEn = new Label
        {
            Text = "EN",
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x47, 0x55, 0x69),
            BackColor = Color.White,
            Left = 676,
            Top = 16,
            Width = 32,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        langEn.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, langEn.Width - 1, langEn.Height - 1);
        };

        var darkToggle = new Label
        {
            Text = "🌙",
            Font = new Font("Segoe UI", 10F),
            ForeColor = TextMuted,
            Left = 716,
            Top = 16,
            Width = 24,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };

        _agentStatusPill = new Label
        {
            Text = "🖧 Ajan: 🗹 • Çalışıyor",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A),
            BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4),
            Left = 746,
            Top = 14,
            Width = 122,
            Height = 28,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _agentStatusPill.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0x86, 0xEF, 0xAC));
            e.Graphics.DrawRectangle(pen, 0, 0, _agentStatusPill.Width - 1, _agentStatusPill.Height - 1);
        };

        headerPanel.Controls.AddRange(new Control[] { logoIcon, brandTitle, versionLabel, langTr, langEn, darkToggle, _agentStatusPill });

        // 2. ALERT BANNER (ORANGE WARNING / GREEN READY)
        _alertBanner = new Panel { Left = 24, Top = 60, Width = 832, Height = 50, BackColor = WarnBg };
        _alertBanner.Paint += (_, e) =>
        {
            var isConn = _getIsConnected();
            var bColor = isConn ? Color.FromArgb(0xBB, 0xF7, 0xD0) : Color.FromArgb(0xFE, 0xD7, 0xAA);
            var barColor = isConn ? Color.FromArgb(0x16, 0xA3, 0x4A) : WarnOrange;
            using var pen = new Pen(bColor);
            e.Graphics.DrawRectangle(pen, 0, 0, _alertBanner.Width - 1, _alertBanner.Height - 1);

            using var brush = new SolidBrush(barColor);
            e.Graphics.FillRectangle(brush, 0, 0, 4, _alertBanner.Height);
        };

        var alertIcon = new Label
        {
            Text = "📶",
            Font = new Font("Segoe UI", 13F),
            ForeColor = WarnOrange,
            Left = 14,
            Top = 12,
            Width = 28,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _alertTitle = new Label
        {
            Text = "SignalR Bağlantısı Kesildi",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = WarnOrange,
            Left = 46,
            Top = 6,
            Width = 620,
            Height = 18
        };

        _alertSubtitle = new Label
        {
            Text = "Sunucuyla gerçek zamanlı iletişim kurulamıyor. Envanter ve metrik verileri gönderilemez.",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(0xC2, 0x41, 0x0C),
            Left = 46,
            Top = 26,
            Width = 620,
            Height = 18
        };

        _alertActionBtn = new Button
        {
            Text = "Bağlan",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = WarnOrange,
            FlatStyle = FlatStyle.Flat,
            Left = 736,
            Top = 9,
            Width = 84,
            Height = 32,
            Cursor = Cursors.Hand
        };
        _alertActionBtn.FlatAppearance.BorderSize = 0;
        _alertActionBtn.Click += (_, _) => RefreshState();

        _alertBanner.Controls.AddRange(new Control[] { alertIcon, _alertTitle, _alertSubtitle, _alertActionBtn });

        // 3. TAB NAVIGATION ROW
        var tabRow = new Panel { Left = 24, Top = 118, Width = 832, Height = 34, BackColor = Color.White };
        
        var tabActive = new Label
        {
            Text = "Bağlantı",
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            ForeColor = AccentBlue,
            Left = 0,
            Top = 4,
            Width = 72,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
        
        var activeLine = new Panel { Left = 0, Top = 29, Width = 72, Height = 3, BackColor = AccentBlue };

        var tab2 = CreateTabLabel("Talepler", 85);
        var tab3 = CreateTabLabel("Envanter", 165);
        var tab4 = CreateTabLabel("Hız Testi", 245);
        var tab5 = CreateTabLabel("Yapılandırma", 325);

        tabRow.Controls.AddRange(new Control[] { tabActive, activeLine, tab2, tab3, tab4, tab5 });

        // 4. HERO STATUS CARD
        _heroCard = new Panel { Left = 24, Top = 158, Width = 832, Height = 68, BackColor = DangerBg };
        _heroCard.Paint += (_, e) =>
        {
            var isConn = _getIsConnected();
            var bColor = isConn ? Color.FromArgb(0xDC, 0xFC, 0xE7) : Color.FromArgb(0xFF, 0xE4, 0xE6);
            using var pen = new Pen(bColor);
            e.Graphics.DrawRectangle(pen, 0, 0, _heroCard.Width - 1, _heroCard.Height - 1);
        };

        _heroIcon = new Label
        {
            Text = "✕",
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            ForeColor = DangerRed,
            BackColor = Color.FromArgb(0xFF, 0xE4, 0xE6),
            Left = 16,
            Top = 14,
            Width = 40,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _heroTitle = new Label
        {
            Text = "Bağlı Değil",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 68,
            Top = 13,
            Width = 600,
            Height = 22
        };

        _heroSubtitle = new Label
        {
            Text = "Sunucuya bağlı değil Lütfen sunucu URL'sini ve bağlantı ayarlarını kontrol edin",
            Font = new Font("Segoe UI", 8.5F),
            ForeColor = Color.FromArgb(0xE1, 0x1D, 0x48),
            Left = 68,
            Top = 36,
            Width = 600,
            Height = 20
        };

        _heroActionBtn = new Button
        {
            Text = "⚡ Bağlan",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            FlatStyle = FlatStyle.Flat,
            Left = 728,
            Top = 16,
            Width = 92,
            Height = 36,
            Cursor = Cursors.Hand
        };
        _heroActionBtn.FlatAppearance.BorderSize = 0;
        _heroActionBtn.Click += (_, _) => RefreshState();

        _heroCard.Controls.AddRange(new Control[] { _heroIcon, _heroTitle, _heroSubtitle, _heroActionBtn });

        // 5. TWO SIDE-BY-SIDE MAIN CARDS
        var cardLeft = CreateCard(24, 236, 406, 360);
        var cardRight = CreateCard(450, 236, 406, 360);

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

        var lblUrl = new Label { Text = "🌐 Sunucu URL ℹ️", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Left = 18, Top = 50, Width = 360, Height = 18 };
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

        var lblToken = new Label { Text = "🔑 Müşteri / Cihaz Token ℹ️", Font = new Font("Segoe UI", 8.5F, FontStyle.Bold), ForeColor = TextMuted, Left = 18, Top = 118, Width = 360, Height = 18 };
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
            Top = 190,
            Width = 175,
            Height = 36,
            Cursor = Cursors.Hand
        };
        btnSaveSettings.FlatAppearance.BorderSize = 0;
        btnSaveSettings.Click += (_, _) =>
        {
            var url = _txtServerUrl.Text.Trim().TrimEnd('/');
            var token = _txtDeviceToken.Text.Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                _saveSettings(url, token);
                MessageBox.Show("Sunucu yapılandırması başarıyla kaydedildi!", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshState();
            }
            else
            {
                MessageBox.Show("Geçerli bir URL adresi giriniz.", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        var btnCopyToken = new Button
        {
            Text = "📋 Token'ı Kopyala",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = SurfaceColor,
            FlatStyle = FlatStyle.Flat,
            Left = 211,
            Top = 190,
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
            Top = 240,
            Width = 368,
            Height = 36,
            Cursor = Cursors.Hand
        };
        btnOpenWeb.FlatAppearance.BorderColor = Color.FromArgb(0xBF, 0xDB, 0xFE);
        btnOpenWeb.Click += (_, _) => _openPanel();

        cardLeft.Controls.AddRange(new Control[] { leftTitle, lblUrl, _txtServerUrl, lblToken, _txtDeviceToken, btnSaveSettings, btnCopyToken, btnOpenWeb });

        // RIGHT CARD: Bağlantı Durumu
        var rightTitle = new Label
        {
            Text = "⚡  Bağlantı Durumu",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 18,
            Top = 16,
            Width = 360,
            Height = 24
        };

        _lblServiceStatus = AddStatusRow(cardRight, 0, "🛡️  NexMote Servisi", "• Çalışıyor", SuccessGreen);
        _lblServerConnStatus = AddStatusRow(cardRight, 1, "🗄️  Sunucu Bağlantısı", "• Bağlı Değil", DangerRed);
        _lblSignalRStatus = AddStatusRow(cardRight, 2, "📶  SignalR Hub", "• Bağlı Değil", DangerRed);

        var btnTestConnection = new Button
        {
            Text = "🗹 Durumu Yenile",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x47, 0x55, 0x69),
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Left = 18,
            Top = 240,
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
            Top = 240,
            Width = 175,
            Height = 38,
            Cursor = Cursors.Hand
        };
        btnCheckUpdates.FlatAppearance.BorderColor = Color.FromArgb(0xBF, 0xDB, 0xFE);
        btnCheckUpdates.Click += (_, _) => _checkUpdates();

        cardRight.Controls.AddRange(new Control[] { rightTitle, btnTestConnection, btnCheckUpdates });

        Controls.AddRange(new Control[] { headerPanel, _alertBanner, tabRow, _heroCard, cardLeft, cardRight });
    }

    private static Label CreateTabLabel(string text, int left)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMuted,
            Left = left,
            Top = 4,
            Width = 75,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };
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
        var y = 54 + index * 56;
        
        var rowBox = new Panel { Left = 18, Top = y, Width = 368, Height = 44, BackColor = SurfaceColor };
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
            Top = 12,
            Width = 200,
            Height = 20
        };

        var val = new Label
        {
            Text = defaultVal,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = defaultColor,
            Left = 210,
            Top = 12,
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
            _agentStatusPill.Text = "🖧 Ajan: 🗹 • Çalışıyor";
            _agentStatusPill.ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A);
            _agentStatusPill.BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4);
        }
        else
        {
            _lblServiceStatus.Text = "• Durdu";
            _lblServiceStatus.ForeColor = DangerRed;
            _agentStatusPill.Text = "🖧 Ajan: ✕ • Durdu";
            _agentStatusPill.ForeColor = DangerRed;
            _agentStatusPill.BackColor = DangerBg;
        }

        if (connected)
        {
            _lblServerConnStatus.Text = "• Bağlı";
            _lblServerConnStatus.ForeColor = SuccessGreen;
            _lblSignalRStatus.Text = "• Bağlı";
            _lblSignalRStatus.ForeColor = SuccessGreen;

            // Alert Banner (Green)
            _alertBanner.BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4);
            _alertTitle.Text = "SignalR Canlı Akış Aktif";
            _alertTitle.ForeColor = Color.FromArgb(0x16, 0x65, 0x34);
            _alertSubtitle.Text = "Sunucu ile gerçek zamanlı iletişim kuruldu. Telemetri ve canlı kontrol hazır.";
            _alertSubtitle.ForeColor = Color.FromArgb(0x15, 0x80, 0x3D);
            _alertActionBtn.Text = "Yenile";
            _alertActionBtn.BackColor = SuccessGreen;

            // Hero Card (Green)
            _heroCard.BackColor = SuccessBg;
            _heroIcon.Text = "✓";
            _heroIcon.ForeColor = SuccessGreen;
            _heroIcon.BackColor = Color.FromArgb(0xDC, 0xFC, 0xE7);
            _heroTitle.Text = "Bağlantı Aktif & Korunuyor";
            _heroSubtitle.Text = "Sunucuya başarıyla bağlanıldı. Arka plan servisi ve canlı sinyalleşme çalışıyor.";
            _heroSubtitle.ForeColor = SuccessGreen;
            _heroActionBtn.Text = "🔄 Yenile";
            _heroActionBtn.BackColor = SuccessGreen;
        }
        else
        {
            _lblServerConnStatus.Text = isServiceRunning ? "• Bağlanıyor" : "• Bağlı Değil";
            _lblServerConnStatus.ForeColor = isServiceRunning ? WarnOrange : DangerRed;
            _lblSignalRStatus.Text = "• Bağlı Değil";
            _lblSignalRStatus.ForeColor = DangerRed;

            // Alert Banner (Orange)
            _alertBanner.BackColor = WarnBg;
            _alertTitle.Text = "SignalR Bağlantısı Kesildi";
            _alertTitle.ForeColor = WarnOrange;
            _alertSubtitle.Text = "Sunucuyla gerçek zamanlı iletişim kurulamıyor. Envanter ve metrik verileri gönderilemez.";
            _alertSubtitle.ForeColor = Color.FromArgb(0xC2, 0x41, 0x0C);
            _alertActionBtn.Text = "Bağlan";
            _alertActionBtn.BackColor = WarnOrange;

            // Hero Card (Red)
            _heroCard.BackColor = DangerBg;
            _heroIcon.Text = "✕";
            _heroIcon.ForeColor = DangerRed;
            _heroIcon.BackColor = Color.FromArgb(0xFF, 0xE4, 0xE6);
            _heroTitle.Text = "Bağlı Değil";
            _heroSubtitle.Text = "Sunucuya bağlı değil Lütfen sunucu URL'sini ve bağlantı ayarlarını kontrol edin";
            _heroSubtitle.ForeColor = Color.FromArgb(0xE1, 0x1D, 0x48);
            _heroActionBtn.Text = "⚡ Bağlan";
            _heroActionBtn.BackColor = AccentBlue;
        }
        _alertBanner.Invalidate();
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
    private int _adaptiveQuality = 85;
    private readonly object _qualityLock = new();
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
            else if (string.Equals(type, "ping", StringComparison.OrdinalIgnoreCase))
            {
                if (_activeSessionId.HasValue && _connection?.State == HubConnectionState.Connected)
                {
                    _ = _connection.InvokeAsync("SendSignal", _activeSessionId.Value, "pong", payload);
                }
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
            _setStatus("guncelleme istegi alindi");
            var prompt = MessageBox.Show(
                "Yönetici tarafından NexMote Ajanı için uzaktan güncelleme emri iletildi.\n\nAjan uygulamasını şimdi yeni sürüme güncellemek istiyor musunuz?",
                "NexMote Ajan Güncelleme Uyarısı",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (prompt == DialogResult.Yes)
            {
                _setStatus("guncelleme paketi indiriliyor...");
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
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(payload);
            if (input is null || _activeSessionId != input.SessionId)
            {
                return;
            }

            // Route input injection to either SYSTEM input-helper (if running/connected)
            // or fallback directly to in-process injection.
            // NEVER execute both, as executing both causes duplicated / multiplied keystrokes and clicks!
            if (!TrySendToInputHelper(payload))
            {
                ApplyInputDirectly(input);
            }
        }
        catch (Exception ex)
        {
            _setStatus($"input uygulanamadi ({ex.Message})");
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
                    _inputHelperPipe.Connect(15);
                    _inputHelperWriter = new StreamWriter(_inputHelperPipe) { AutoFlush = true };
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
                _nextPipeConnectAttemptTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
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

            using var http = new HttpClient();
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
        var forceIntervalTicks = Stopwatch.Frequency * 3; // En geç 3 saniyede bir zorunlu senkronizasyon karesi
        var lastSendTicks = 0L;
        var lastMotionTicks = Stopwatch.GetTimestamp();
        var refinementSent = false;

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
                var now = Stopwatch.GetTimestamp();
                var forceSend = (now - lastSendTicks) >= forceIntervalTicks;
                if (forceSend)
                {
                    ScreenCapture.ResetHash(displayIndex);
                    refinementSent = false;
                }

                // Hareket durduğunda kristal netlikte iyileştirme karesi (AnyDesk / RustDesk Static Refinement)
                var timeSinceMotionMs = (now - lastMotionTicks) * 1000 / Stopwatch.Frequency;
                var isRefinement = !refinementSent && timeSinceMotionMs > 180 && (now - lastSendTicks) > 0;

                int quality;
                if (isRefinement)
                {
                    quality = 94; // Kristal netlikte statik görüntü (metin ve kodlar jilet gibi keskin)
                    forceSend = true;
                }
                else
                {
                    quality = GetCurrentQuality(); // Akıcı hareket akışı (75-88 arası)
                }

                var frame = ScreenCapture.CaptureJpegBase64(displayIndex, quality, forceSend);

                if (frame is not null && _connection?.State == HubConnectionState.Connected)
                {
                    var payload = JsonSerializer.Serialize(new MultiScreenFrame(displayIndex, frame));
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

                await Task.Delay(TimeSpan.FromMilliseconds(33), cancellationToken); // ~30 FPS
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

    private int GetCurrentQuality()
    {
        lock (_qualityLock)
        {
            return _adaptiveQuality;
        }
    }

    private void AdjustQuality(long elapsedMs)
    {
        lock (_qualityLock)
        {
            if (elapsedMs > 180 && _adaptiveQuality > 68)
            {
                _adaptiveQuality = Math.Max(68, _adaptiveQuality - 4);
            }
            else if (elapsedMs < 60 && _adaptiveQuality < 90)
            {
                _adaptiveQuality = Math.Min(90, _adaptiveQuality + 2);
            }
        }
    }

    /// <summary>
    /// Downloads the new agent MSI and drops it where the NexMote Agent Windows Service (running
    /// as LocalSystem) polls for it.
    /// </summary>
    public static async Task PerformSelfUpdateAsync(string msiUrl)
    {
        try
        {
            var programDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
            Directory.CreateDirectory(programDataDir);
            var pendingMsi = Path.Combine(programDataDir, "pending-update.msi");

            using var http = new HttpClient();
            var bytes = await http.GetByteArrayAsync(msiUrl);
            await File.WriteAllBytesAsync(pendingMsi, bytes);
        }
        catch
        {
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
/// %ProgramData%\NexMote\Agent\identity.json dosyasından cihaz kimliğini ve token'ını okuyan yardımcı sınıf.
/// </summary>
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

    public static async Task<DeviceIdentity?> EnsureEnrolledAsync(string serverUrl, string enrollmentKey)
    {
        var existing = Load();
        if (existing is not null) return existing;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var enrollUrl = $"{serverUrl.TrimEnd('/')}/api/agents/enroll";

            var os = Environment.OSVersion.VersionString;
            var deviceName = Environment.MachineName;
            var domainName = Environment.UserDomainName;
            var activeUser = Environment.UserName;
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.5.4";

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
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dir = Path.Combine(programData, "NexMote", "Agent");
            Directory.CreateDirectory(dir);
            var identityPath = Path.Combine(dir, "identity.json");
            File.WriteAllText(identityPath, JsonSerializer.Serialize(identity, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

internal sealed record DeviceIdentity(Guid DeviceId, string AgentToken);

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

    public static void ResetHash(int displayIndex)
    {
        LastFrameHashes[displayIndex] = 0;
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
        using var targetBitmap = ResizeIfNeeded(capture, 3840);
        using var stream = new MemoryStream();
        SaveJpeg(targetBitmap, stream, Math.Clamp((long)quality, 40, 96));

        return Convert.ToBase64String(stream.ToArray());
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

    private static Bitmap ResizeIfNeeded(Bitmap source, int maxWidth = 3840)
    {
        // 1080p, 2K (1440p) ve 4K (2160p) ekranları ASLA küçültme, 1:1 piksel netliğinde koru
        if (source.Width <= maxWidth)
        {
            return (Bitmap)source.Clone();
        }

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
        var encoder = GetEncoder(ImageFormat.Jpeg);
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    public static void AttachToActiveDesktop()
    {
        try
        {
            var hWinSta = OpenWindowStation("winsta0", false, GENERIC_ALL);
            if (hWinSta != IntPtr.Zero)
            {
                SetProcessWindowStation(hWinSta);
                CloseWindowStation(hWinSta);
            }

            var hDesktop = OpenInputDesktop(0, false, GENERIC_ALL);
            if (hDesktop != IntPtr.Zero)
            {
                SetThreadDesktop(hDesktop);
                CloseDesktop(hDesktop);
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
        using var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        if (!createdNew)
        {
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
                    maxNumberOfServerInstances: 1,
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

                using var reader = new StreamReader(server);
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
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(json);
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
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
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

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static bool IsExtendedKey(int keyCode)
    {
        return keyCode is 37 or 38 or 39 or 40 or 33 or 34 or 35 or 36 or 45 or 46 or 91 or 92;
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
    public static Task<CommandRunResult> RunAsync(string shell, string command, int timeoutMs, bool runAsAdmin)
    {
        return runAsAdmin
            ? RunElevatedAsync(shell, command, timeoutMs)
            : RunStandardAsync(shell, command, timeoutMs);
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

        var arguments = isPowerShell
            ? $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{command.Replace("\"", "\\\"")} *>&1 | Out-File -FilePath '{outFile}' -Encoding utf8\""
            : $"/c \"{command} > \"{outFile}\" 2>&1\"";

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
        if (string.IsNullOrWhiteSpace(raw) || raw.Contains("192.168.0") || raw.Contains("127.0.0.1") || raw.Contains("localhost") || raw.StartsWith("http://"))
        {
            raw = "https://nexmote.com";
            try
            {
                SaveSettings(raw, LoadEnrollmentKey());
            }
            catch { }
        }
        return raw;
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
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var agentDir = Path.Combine(programData, "NexMote", "Agent");
        Directory.CreateDirectory(agentDir);

        var serviceConfigPath = Path.Combine(agentDir, "appsettings.json");
        SaveConfigToPath(serviceConfigPath, newUrl, newKey);

        var baseConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        SaveConfigToPath(baseConfigPath, newUrl, newKey);
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
