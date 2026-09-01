using System.Drawing.Drawing2D;
using NexMote.Shared.Contracts;
using NexMote.Shared.Network;

namespace NexMote.Agent.Tray;

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

        var versionStr = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.7.0";

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
            path.AddArc(32 - r * 2, 32 - r * 2, r * 2, 2 * r, 0, 90);
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
            Text = $"NexMote Agent  v{versionStr}",
            Font = new Font("Segoe UI", 13.5F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 66,
            Top = 13,
            Width = 350,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft
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

        headerPanel.Controls.AddRange(new Control[] { logoIcon, brandTitle, _agentStatusPill });

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

        // LEFT CARD: Cihaz ve Sunucu Bilgileri
        var leftTitle = new Label
        {
            Text = "🖥️  Cihaz & Sunucu Bilgileri",
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

        AddStatusRow(cardLeft, 1, "Bilgisayar Adı", Environment.MachineName, TextDark);
        AddStatusRow(cardLeft, 2, "Aktif Kullanıcı", Environment.UserName, TextDark);
        AddStatusRow(cardLeft, 3, "Kurulum Modeli", "• Sıfır-Kodlu", SuccessGreen);

        var btnSaveSettings = new Button
        {
            Text = "💾 Sunucu URL Kaydet",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            FlatStyle = FlatStyle.Flat,
            Left = 18,
            Top = 250,
            Width = 175,
            Height = 38,
            Cursor = Cursors.Hand
        };
        btnSaveSettings.FlatAppearance.BorderSize = 0;
        btnSaveSettings.Click += (_, _) =>
        {
            var url = NexMoteHttp.NormalizeUrl(_txtServerUrl.Text);
            _txtServerUrl.Text = url;
            _saveSettings(url, AgentSettings.LoadEnrollmentKey());
            MessageBox.Show("Sunucu adresi başarıyla kaydedildi!", "NexMote", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshState();
        };

        var btnOpenWeb = new Button
        {
            Text = "🌐 Web Panelini Aç",
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
        btnOpenWeb.FlatAppearance.BorderColor = Color.FromArgb(0xBF, 0xDB, 0xFE);
        btnOpenWeb.Click += (_, _) => _openPanel();

        cardLeft.Controls.AddRange(new Control[] { leftTitle, lblUrl, _txtServerUrl, btnSaveSettings, btnOpenWeb });

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

        _lblServiceStatus = AddStatusRow(cardRight, 0, "NexMote Servisi", "• Çalışıyor", SuccessGreen);
        _lblServerConnStatus = AddStatusRow(cardRight, 1, "Sunucu Bağlantısı", "• Bağlı", SuccessGreen);
        _lblSignalRStatus = AddStatusRow(cardRight, 2, "SignalR Canlı Akış", "• Bağlı", SuccessGreen);
        AddStatusRow(cardRight, 3, "Yüklü Ajan Sürümü", $"v{versionStr}", AccentBlue);

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
            Width = 160,
            Height = 20,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var val = new Label
        {
            Text = defaultVal,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = defaultColor,
            Left = 175,
            Top = 10,
            Width = 182,
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
