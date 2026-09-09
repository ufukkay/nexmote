using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace NexMote.Agent.Tray;

/// <summary>
/// DirectX 11 Desktop Duplication API (DXGI) kullanarak ekran görüntülerini doğrudan GPU VRAM'den
/// sıfır işlemci (CPU) yüküyle yakalayan yüksek performanslı ekran yakalama motoru.
/// </summary>
internal sealed class DxgiScreenCapture : IDisposable
{
    private static readonly object SyncLock = new();
    private static DxgiScreenCapture? _instance;

    public static DxgiScreenCapture Instance
    {
        get
        {
            if (_instance is null)
            {
                lock (SyncLock)
                {
                    _instance ??= new DxgiScreenCapture();
                }
            }
            return _instance;
        }
    }

    private readonly Dictionary<int, DisplayDuplicationContext> _contexts = new();
    private bool _isDisposed;

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

    private sealed class DisplayDuplicationContext : IDisposable
    {
        public ID3D11Device Device { get; }
        public ID3D11DeviceContext Context { get; }
        public IDXGIOutputDuplication Duplication { get; }
        public ID3D11Texture2D StagingTexture { get; }
        public int Width { get; }
        public int Height { get; }
        public Rectangle ScreenBounds { get; }

        public DisplayDuplicationContext(
            ID3D11Device device,
            ID3D11DeviceContext context,
            IDXGIOutputDuplication duplication,
            ID3D11Texture2D stagingTexture,
            int width,
            int height,
            Rectangle screenBounds)
        {
            Device = device;
            Context = context;
            Duplication = duplication;
            StagingTexture = stagingTexture;
            Width = width;
            Height = height;
            ScreenBounds = screenBounds;
        }

        public void Dispose()
        {
            try { StagingTexture.Dispose(); } catch { }
            try { Duplication.Dispose(); } catch { }
            try { Context.Dispose(); } catch { }
            try { Device.Dispose(); } catch { }
        }
    }

    private DxgiScreenCapture()
    {
    }

    public static bool IsSupported()
    {
        try
        {
            var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            var result = D3D11.D3D11CreateDevice(
                IntPtr.Zero,
                DriverType.Hardware,
                DeviceCreationFlags.BgraSupport,
                levels,
                out var device,
                out var context);

            if (result.Success && device is not null)
            {
                using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();
                var hasOutput = adapter.EnumOutputs(0, out var output).Success && output is not null;
                output?.Dispose();
                context?.Dispose();
                device.Dispose();
                return hasOutput;
            }
        }
        catch
        {
        }
        return false;
    }

    private DisplayDuplicationContext? GetOrCreateContext(int displayIndex)
    {
        if (_isDisposed) return null;

        lock (SyncLock)
        {
            if (_contexts.TryGetValue(displayIndex, out var existing))
            {
                return existing;
            }

            try
            {
                var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
                var devRes = D3D11.D3D11CreateDevice(
                    IntPtr.Zero,
                    DriverType.Hardware,
                    DeviceCreationFlags.BgraSupport,
                    levels,
                    out var device,
                    out var context);

                if (!devRes.Success || device is null || context is null)
                {
                    return null;
                }

                using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
                using var adapter = dxgiDevice.GetAdapter();

                // Windows Display Index to 0-based adapter output index
                var targetOutputIdx = Math.Max(0, displayIndex - 1);
                var enumRes = adapter.EnumOutputs(targetOutputIdx, out var output);
                if (!enumRes.Success || output is null)
                {
                    // Fallback to output 0
                    enumRes = adapter.EnumOutputs(0, out output);
                    if (!enumRes.Success || output is null)
                    {
                        context.Dispose();
                        device.Dispose();
                        return null;
                    }
                }

                using (output)
                {
                    using var output1 = output.QueryInterface<IDXGIOutput1>();
                    if (output1 is null)
                    {
                        context.Dispose();
                        device.Dispose();
                        return null;
                    }

                    var duplication = output1.DuplicateOutput(device);
                    if (duplication is null)
                    {
                        context.Dispose();
                        device.Dispose();
                        return null;
                    }

                    var desc = duplication.Description;
                    var width = desc.ModeDescription.Width;
                    var height = desc.ModeDescription.Height;
                    var bounds = new Rectangle(0, 0, width, height);

                    // Multi-monitor coordinate detection from Screen.AllScreens
                    var screens = Screen.AllScreens;
                    if (targetOutputIdx < screens.Length)
                    {
                        bounds = screens[targetOutputIdx].Bounds;
                    }

                    var stagingDesc = new Texture2DDescription
                    {
                        Width = width,
                        Height = height,
                        MipLevels = 1,
                        ArraySize = 1,
                        Format = Format.B8G8R8A8_UNorm,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging,
                        BindFlags = BindFlags.None,
                        CPUAccessFlags = CpuAccessFlags.Read,
                        MiscFlags = ResourceOptionFlags.None
                    };

                    var stagingTexture = device.CreateTexture2D(stagingDesc);
                    if (stagingTexture is null)
                    {
                        duplication.Dispose();
                        context.Dispose();
                        device.Dispose();
                        return null;
                    }

                    var newCtx = new DisplayDuplicationContext(device, context, duplication, stagingTexture, width, height, bounds);
                    _contexts[displayIndex] = newCtx;
                    return newCtx;
                }
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// DXGI Desktop Duplication kullanarak ekran karesini JPEG byte dizisi olarak yakalar.
    /// Ekran değişmemişse (statik ekran - DXGI timeout) null döner (sıfır CPU!).
    /// Masaüstü değişimi (UAC/Kilit) veya hata durumunda capturedWithDxgi=false set eder.
    /// </summary>
    public byte[]? CaptureJpegBytes(int displayIndex, int quality, bool forceSend, out bool capturedWithDxgi)
    {
        capturedWithDxgi = false;
        var ctx = GetOrCreateContext(displayIndex);
        if (ctx is null)
        {
            return null;
        }

        try
        {
            // 20ms timeout: Ekran değişmemişse DXGI anında döner (0 ms gecikme, 0 CPU!)
            var timeoutMs = forceSend ? 100 : 25;
            var acquireRes = ctx.Duplication.AcquireNextFrame(timeoutMs, out var frameInfo, out var desktopResource);

            if (acquireRes.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
            {
                // Ekran statik, piksel değişikliği yok
                if (!forceSend)
                {
                    capturedWithDxgi = true;
                    return null;
                }

                // forceSend=true (ilk bağlantı anı veya periyodik anahtar kare):
                // Ekranda hareket henüz yoksa DXGI timeout döner. Teknisyenin 'Görüntü akışı bekleniyor'
                // ekranında takılı kalmaması için GDI+ fallback'e devret ki hemen tam bir kare yakalanıp gönderilsin:
                capturedWithDxgi = false;
                return null;
            }

            if (!acquireRes.Success || desktopResource is null)
            {
                // AccessLost, SessionDisconnected vb. Masaüstü değişti (UAC, Kilit, Çözünürlük)
                DesktopHelper.AttachToActiveDesktop(force: true);
                ResetDisplay(displayIndex);
                capturedWithDxgi = false;
                return null;
            }

            capturedWithDxgi = true;

            using (desktopResource)
            using (var desktopTexture = desktopResource.QueryInterface<ID3D11Texture2D>())
            {
                if (desktopTexture is null)
                {
                    ctx.Duplication.ReleaseFrame();
                    return null;
                }

                // GPU VRAM'den CPU Staging Texture'a doğrudan transfer
                ctx.Context.CopyResource(ctx.StagingTexture, desktopTexture);
            }

            // Staging texture'ı belleğe eşle (Map)
            var mapped = ctx.Context.Map(ctx.StagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
            try
            {
                // Doğrudan bellek işaretçisi (Zero-Copy) üzerinden 32bppArgb Bitmap sarmalayıcısı
                using var bmp = new Bitmap(ctx.Width, ctx.Height, mapped.RowPitch, PixelFormat.Format32bppArgb, mapped.DataPointer);

                // İmleç katmanını çiz
                DrawCursorIfShowing(bmp, ctx.ScreenBounds);

                // Bellekten doğrudan JPEG sıkıştırması
                using var ms = new MemoryStream(128 * 1024);
                ScreenCapture.SaveJpegToStream(bmp, ms, quality);
                return ms.ToArray();
            }
            finally
            {
                ctx.Context.Unmap(ctx.StagingTexture, 0);
                ctx.Duplication.ReleaseFrame();
            }
        }
        catch
        {
            ResetDisplay(displayIndex);
            return null;
        }
    }

    /// <summary>
    /// DXGI ile yakalayıp Base64 JPEG string döner.
    /// </summary>
    public string? CaptureJpegBase64(int displayIndex, int quality, bool forceSend, out bool capturedWithDxgi)
    {
        var bytes = CaptureJpegBytes(displayIndex, quality, forceSend, out capturedWithDxgi);
        return bytes is not null ? Convert.ToBase64String(bytes) : null;
    }

    private static void DrawCursorIfShowing(Bitmap bitmap, Rectangle screenBounds)
    {
        try
        {
            var pci = new CURSORINFO { cbSize = Marshal.SizeOf(typeof(CURSORINFO)) };
            if (GetCursorInfo(out pci) && pci.flags == CURSOR_SHOWING && pci.hCursor != IntPtr.Zero)
            {
                var cursorX = pci.ptScreenPos.x - screenBounds.Left;
                var cursorY = pci.ptScreenPos.y - screenBounds.Top;
                if (cursorX >= -32 && cursorX < bitmap.Width && cursorY >= -32 && cursorY < bitmap.Height)
                {
                    using var g = Graphics.FromImage(bitmap);
                    var hdc = g.GetHdc();
                    try
                    {
                        DrawIcon(hdc, cursorX, cursorY, pci.hCursor);
                    }
                    finally
                    {
                        g.ReleaseHdc(hdc);
                    }
                }
            }
        }
        catch { }
    }

    private void ResetDisplay(int displayIndex)
    {
        lock (SyncLock)
        {
            if (_contexts.Remove(displayIndex, out var ctx))
            {
                try { ctx.Dispose(); } catch { }
            }
        }
    }

    public void Reset()
    {
        lock (SyncLock)
        {
            foreach (var ctx in _contexts.Values)
            {
                try { ctx.Dispose(); } catch { }
            }
            _contexts.Clear();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Reset();
    }
}
