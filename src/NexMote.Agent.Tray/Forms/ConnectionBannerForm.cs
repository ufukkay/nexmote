namespace NexMote.Agent.Tray;

/// <summary>
/// Uzaktan oturum açıkken masaüstünün sağ üst köşesinde görünen küçük, odak çalmayan bildirim rozeti.
/// </summary>
internal sealed class ConnectionBannerForm : Form
{
    public ConnectionBannerForm(string? title = null)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.FromArgb(240, 244, 248);
        Size = new Size(270, 36);

        var primary = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(primary.Right - 285, primary.Top + 12);

        var lbl = new Label
        {
            Text = $"🛡️ {title ?? "NexMote"}: Teknisyen Bağlı",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 222, 128),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(lbl);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }
}
