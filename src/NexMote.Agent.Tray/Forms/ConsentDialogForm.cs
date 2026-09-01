using NexMote.Shared.Contracts;

namespace NexMote.Agent.Tray;

/// <summary>
/// Teknisyen bağlandığında hedef bilgisayardaki kullanıcının karşısına çıkan geri sayımlı bağlantı onay formu.
/// </summary>
internal sealed class ConsentDialogForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private int _remainingSeconds;
    private readonly string _defaultAction;
    private readonly Label _lblTimer;
    public bool Accepted { get; private set; }

    public ConsentDialogForm(string technicianName, int timeoutSeconds, string defaultAction)
    {
        _remainingSeconds = timeoutSeconds > 0 ? timeoutSeconds : 30;
        _defaultAction = defaultAction;

        Text = "NexMote - Uzaktan Bağlantı İsteği";
        Width = 440;
        Height = 225;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        BackColor = Color.FromArgb(20, 24, 33);
        ForeColor = Color.FromArgb(240, 244, 248);
        Icon = IconHelper.GetAppIcon();

        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = Color.FromArgb(15, 18, 25),
            Padding = new Padding(16, 12, 16, 0)
        };
        var lblTitle = new Label
        {
            Text = "🛡️ Uzaktan Bağlantı Onayı",
            Font = new Font("Segoe UI", 11.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 222, 128),
            AutoSize = true
        };
        pnlHeader.Controls.Add(lblTitle);

        var lblMsg = new Label
        {
            Text = $"Teknisyen [{technicianName}] bilgisayarınıza uzaktan bağlanmak istiyor.\n\nBağlantıya izin veriyor musunuz?",
            Font = new Font("Segoe UI", 9.5F),
            ForeColor = Color.FromArgb(220, 225, 235),
            Location = new Point(18, 58),
            Size = new Size(400, 52)
        };

        var defText = string.Equals(_defaultAction, SecurityProfileConstants.ActionAllow, StringComparison.OrdinalIgnoreCase) ? "Otomatik Kabul" : "Otomatik Reddet";
        _lblTimer = new Label
        {
            Text = $"Kalan süre: {_remainingSeconds} sn ({defText})",
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(18, 115),
            AutoSize = true
        };

        var btnAccept = new Button
        {
            Text = "✔ Kabul Et",
            DialogResult = DialogResult.OK,
            Location = new Point(190, 142),
            Size = new Size(110, 32),
            BackColor = Color.FromArgb(34, 197, 94),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnAccept.FlatAppearance.BorderSize = 0;
        btnAccept.Click += (_, _) => { Accepted = true; Close(); };

        var btnReject = new Button
        {
            Text = "✖ Reddet",
            DialogResult = DialogResult.Cancel,
            Location = new Point(310, 142),
            Size = new Size(100, 32),
            BackColor = Color.FromArgb(239, 68, 68),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        btnReject.FlatAppearance.BorderSize = 0;
        btnReject.Click += (_, _) => { Accepted = false; Close(); };

        Controls.Add(pnlHeader);
        Controls.Add(lblMsg);
        Controls.Add(_lblTimer);
        Controls.Add(btnAccept);
        Controls.Add(btnReject);

        AcceptButton = btnAccept;
        CancelButton = btnReject;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _remainingSeconds--;
            if (_remainingSeconds <= 0)
            {
                _timer.Stop();
                Accepted = string.Equals(_defaultAction, SecurityProfileConstants.ActionAllow, StringComparison.OrdinalIgnoreCase);
                Close();
            }
            else
            {
                _lblTimer.Text = $"Kalan süre: {_remainingSeconds} sn ({defText})";
            }
        };
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _timer.Dispose();
        base.OnFormClosed(e);
    }
}
