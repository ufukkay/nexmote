using System.Drawing.Drawing2D;

namespace NexMote.Agent.Tray;

/// <summary>
/// Antivirüs yazılımlarına benzer modern, minimalist ve sade durum paneli formu.
/// Son kullanıcıyı gereksiz teknik ayrıntılarla boğmadan güvenli bağlantı durumunu gösterir.
/// </summary>
internal sealed class DashboardForm : Form
{
    private static readonly Color AccentBlue = Color.FromArgb(0x25, 0x63, 0xEB);
    private static readonly Color TextDark = Color.FromArgb(0x0F, 0x17, 0x2A);
    private static readonly Color TextMuted = Color.FromArgb(0x64, 0x74, 0x8B);
    private static readonly Color BorderColor = Color.FromArgb(0xE2, 0xE8, 0xF0);
    private static readonly Color SurfaceColor = Color.FromArgb(0xF8, 0xFA, 0xFC);
    private static readonly Color CardBg = Color.FromArgb(0xFF, 0xFF, 0xFF);
    private static readonly Color SuccessGreen = Color.FromArgb(0x10, 0xB9, 0x81);
    private static readonly Color SuccessBg = Color.FromArgb(0xEC, 0xFD, 0xF5);
    private static readonly Color WarnOrange = Color.FromArgb(0xEA, 0x58, 0x0C);
    private static readonly Color WarnBg = Color.FromArgb(0xFF, 0xF7, 0xED);

    private readonly Func<bool> _getIsConnected;
    private readonly Action _refresh;

    // UI Controls
    private Panel _heroCard = null!;
    private Label _heroIcon = null!;
    private Label _heroTitle = null!;
    private Label _heroSubtitle = null!;
    private Label _agentStatusPill = null!;

    public DashboardForm(
        Func<bool> getIsConnected,
        Action refresh)
    {
        _getIsConnected = getIsConnected;
        _refresh = refresh;

        Text = "NexMote Agent Durum Paneli";
        ClientSize = new Size(500, 360);
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
        var headerPanel = new Panel { Left = 0, Top = 0, Width = 500, Height = 52, BackColor = Color.White };
        headerPanel.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 0, 51, 500, 51);
        };

        var logoIcon = new Label
        {
            Text = "N",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = AccentBlue,
            Left = 20,
            Top = 11,
            Width = 30,
            Height = 30,
            TextAlign = ContentAlignment.MiddleCenter
        };
        logoIcon.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(AccentBlue);
            using var path = new GraphicsPath();
            int r = 5;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(30 - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(30 - r * 2, 30 - r * 2, r * 2, 2 * r, 0, 90);
            path.AddArc(0, 30 - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, path);

            using var font = new Font("Segoe UI", 12F, FontStyle.Bold);
            using var textBrush = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("N", font, textBrush, new RectangleF(0, 0, 30, 30), sf);
        };

        var brandTitle = new Label
        {
            Text = $"NexMote Agent  v{versionStr}",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 58,
            Top = 10,
            Width = 260,
            Height = 32,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _agentStatusPill = new Label
        {
            Text = "• Ajan Aktif",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A),
            BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4),
            Left = 360,
            Top = 13,
            Width = 120,
            Height = 26,
            TextAlign = ContentAlignment.MiddleCenter
        };
        _agentStatusPill.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(0x86, 0xEF, 0xAC));
            e.Graphics.DrawRectangle(pen, 0, 0, _agentStatusPill.Width - 1, _agentStatusPill.Height - 1);
        };

        headerPanel.Controls.AddRange(new Control[] { logoIcon, brandTitle, _agentStatusPill });

        // 2. HERO STATUS CARD (Large status display)
        _heroCard = new Panel { Left = 20, Top = 68, Width = 460, Height = 115, BackColor = SuccessBg };
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
            Font = new Font("Segoe UI Emoji", 26F),
            Left = 18,
            Top = 22,
            Width = 55,
            Height = 65,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _heroTitle = new Label
        {
            Text = "Uzaktan Destek Hizmeti Hazır",
            Font = new Font("Segoe UI", 12F, FontStyle.Bold),
            ForeColor = TextDark,
            Left = 82,
            Top = 20,
            Width = 360,
            Height = 26
        };

        _heroSubtitle = new Label
        {
            Text = "Teknisyeniniz gerektiğinde bu bilgisayara güvenli ve şifreli uzaktan erişim sağlayabilir.",
            Font = new Font("Segoe UI", 9F),
            ForeColor = TextMuted,
            Left = 82,
            Top = 48,
            Width = 360,
            Height = 50
        };

        _heroCard.Controls.AddRange(new Control[] { _heroIcon, _heroTitle, _heroSubtitle });

        // 3. ESSENTIAL INFO CARD (2 rows only: DeviceName & ActiveUser)
        var infoCard = new Panel { Left = 20, Top = 196, Width = 460, Height = 94, BackColor = CardBg };
        infoCard.Paint += (_, e) =>
        {
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawRectangle(pen, 0, 0, infoCard.Width - 1, infoCard.Height - 1);
            e.Graphics.DrawLine(pen, 12, 47, 448, 47);
        };

        var row1Label = new Label { Text = "Bilgisayar Adı", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = TextDark, Left = 16, Top = 14, Width = 140, Height = 20 };
        var row1Val = new Label { Text = Environment.MachineName, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = TextDark, Left = 160, Top = 14, Width = 284, Height = 20, TextAlign = ContentAlignment.MiddleRight };

        var row2Label = new Label { Text = "Aktif Kullanıcı", Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = TextDark, Left = 16, Top = 60, Width = 140, Height = 20 };
        var row2Val = new Label { Text = Environment.UserName, Font = new Font("Segoe UI", 9F, FontStyle.Bold), ForeColor = TextDark, Left = 160, Top = 60, Width = 284, Height = 20, TextAlign = ContentAlignment.MiddleRight };

        infoCard.Controls.AddRange(new Control[] { row1Label, row1Val, row2Label, row2Val });

        // 4. BOTTOM ACTION BAR
        var btnClose = new Button
        {
            Text = "Kapat",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextDark,
            BackColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Left = 380,
            Top = 306,
            Width = 100,
            Height = 34,
            Cursor = Cursors.Hand
        };
        btnClose.FlatAppearance.BorderColor = BorderColor;
        btnClose.Click += (_, _) => Hide();

        Controls.AddRange(new Control[] { headerPanel, _heroCard, infoCard, btnClose });
    }

    public void RefreshState()
    {
        _refresh();

        var connected = _getIsConnected();

        if (connected)
        {
            _agentStatusPill.Text = "• Ajan Aktif";
            _agentStatusPill.ForeColor = Color.FromArgb(0x16, 0xA3, 0x4A);
            _agentStatusPill.BackColor = Color.FromArgb(0xF0, 0xFD, 0xF4);

            _heroCard.BackColor = SuccessBg;
            _heroIcon.Text = "🛡️";
            _heroTitle.Text = "Uzaktan Destek Hizmeti Hazır";
            _heroSubtitle.Text = "Teknisyeniniz gerektiğinde bu bilgisayara güvenli ve şifreli uzaktan erişim sağlayabilir.";
        }
        else
        {
            _agentStatusPill.Text = "• Bağlanıyor";
            _agentStatusPill.ForeColor = WarnOrange;
            _agentStatusPill.BackColor = WarnBg;

            _heroCard.BackColor = WarnBg;
            _heroIcon.Text = "📶";
            _heroTitle.Text = "Bağlantı Kuruluyor...";
            _heroSubtitle.Text = "Sunucu bağlantısı kuruluyor. Otomatik olarak yeniden bağlanılacaktır.";
        }

        _heroCard.Invalidate();
        _agentStatusPill.Invalidate();
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
