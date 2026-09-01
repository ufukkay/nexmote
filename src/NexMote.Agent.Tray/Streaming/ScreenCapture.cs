using System.Collections.Concurrent;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using NexMote.Shared.Contracts;

namespace NexMote.Agent.Tray;

/// <summary>
/// Windows masaüstü ekran görüntülerini, fare imlecini (cursor) GDI+ / BitBlt kullanarak yakalayan,
/// donanımsal 1:1 piksel netliğini koruyan ve JPEG öncesi ham bellek hash kontrolüyle sıfır CPU tüketen ekran yakalama motoru.
/// </summary>
internal static class ScreenCapture
{
    private static readonly ConcurrentDictionary<int, ulong> LastFrameHashes = new();

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

        var rawHash = ComputeBitmapHash(capture);
        if (!forceSend && LastFrameHashes.TryGetValue(displayIndex, out var lastHash) && rawHash == lastHash)
        {
            return null;
        }

        LastFrameHashes[displayIndex] = rawHash;

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
            var step = Math.Max(1, totalBytes / 1024);
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
