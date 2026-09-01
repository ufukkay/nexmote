using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NexMote.Agent.Tray;

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
