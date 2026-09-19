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

    // DİKKAT: Bazı GPU sürücülerinde (üretimde kanıtlandı: Intel Iris Xe igd10um64xe.DLL 32.0.101.7088)
    // DXGI Desktop Duplication, sadece kilit ekranı geçişlerinde değil GENEL kullanımda da native bir
    // erişim ihlaliyle (AccessViolation, C# try/catch ile YAKALANAMAZ) tüm Tray sürecini çökertebiliyor.
    // Süreç zaten öldüğü için ÇÖKME SONRASI kendi içinde bunu tespit edip devre dışı bırakamaz — bu yüzden
    // diske yazılan basit bir "art arda çökme sayacı" ile kalıcı devre kesici (circuit breaker) uyguluyoruz:
    // bu süreç DXGI'yi denemeden HEMEN ÖNCE sayaç diske yazılıyor; eğer süreç anormal şekilde ölürse bir
    // sonraki başlatmada sayaç hâlâ yüksek görülür ve DXGI o andan itibaren KALICI olarak (GDI+'ya
    // düşülerek) devre dışı bırakılır. Süreç DXGI ile 3 dakika sorunsuz çalışırsa sayaç sıfırlanır.
    private const int MaxCrashStrikesBeforeDisable = 1;
    private static readonly string HealthFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent", "dxgi-health.txt");
    private static bool? _permanentlyDisabled;
    private static System.Threading.Timer? _stabilityTimer;

    /// <summary>
    /// DXGI'nin bu makinede kalıcı olarak devre dışı bırakılıp bırakılmadığını döner (bkz. yukarıdaki açıklama).
    /// İlk çağrıda henüz devre dışı değilse "bu oturumda deneniyor" sayacını diske yazar ve bir kararlılık
    /// zamanlayıcısı kurar; sonucu süreç ömrü boyunca önbelleğe alır (tek seferlik I/O).
    /// </summary>
    public static bool IsPermanentlyDisabled()
    {
        if (_permanentlyDisabled.HasValue)
        {
            return _permanentlyDisabled.Value;
        }

        try
        {
            var dir = Path.GetDirectoryName(HealthFilePath)!;
            Directory.CreateDirectory(dir);

            var strikes = 0;
            if (File.Exists(HealthFilePath) && int.TryParse(File.ReadAllText(HealthFilePath).Trim(), out var parsed))
            {
                strikes = parsed;
            }

            if (strikes >= MaxCrashStrikesBeforeDisable)
            {
                _permanentlyDisabled = true;
                return true;
            }

            File.WriteAllText(HealthFilePath, (strikes + 1).ToString());
            _permanentlyDisabled = false;

            _stabilityTimer = new System.Threading.Timer(_ =>
            {
                try { File.WriteAllText(HealthFilePath, "0"); } catch { }
            }, null, TimeSpan.FromMinutes(3), System.Threading.Timeout.InfiniteTimeSpan);

            return false;
        }
        catch
        {
            return false;
        }
    }

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

                // DİKKAT: DXGI'nin çıktı numaralandırma sırası (adapter.EnumOutputs) ile WinForms'un
                // Screen.AllScreens sırası AYNI FİZİKSEL MONİTÖRE denk gelmek ZORUNDA DEĞİL. Çoklu
                // monitörlü makinelerde ham index eşleştirmesi (displayIndex - 1) yanlış monitörü
                // yakalayabiliyordu — bu da teknisyenin gönderdiği fare koordinatının (doğru monitörün
                // JPEG kareler üzerinden hesaplanan) InputInjector tarafında YANLIŞ monitörün/offsetin
                // sınırlarına göre yorumlanmasına, yani "burada tıklıyorum, imleç orada beliriyor"
                // şikayetine yol açıyordu. Bunun yerine Win32 aygıt adını (ör. "\\.\DISPLAY1") eşleştirip
                // doğru çıktıyı seçiyoruz; eşleşme bulunamazsa eski davranışa (index) düşüyoruz.
                var targetOutputIdx = Math.Max(0, displayIndex - 1);

                // Tek monitörlü makinelerde (Screen.AllScreens.Length <= 1) belirsizlik zaten yok —
                // index 0 her zaman TEK çıktıya denk gelir. Bu durumda aşağıdaki aygıt adı eşleştirme
                // döngüsüne HİÇ girmiyoruz: üretimde bu döngü (COM/DXGI enumOutputs çağrılarını normalden
                // daha sık ve farklı bir örüntüyle tetikleyerek) bazı Intel iGPU sürücülerinde
                // NexMote.Agent.Tray.exe sürecinin kendisinde erişim ihlaline (AccessViolation) yol açtı —
                // özellikle kilit ekranı açılış/kapanış geçişlerinde art arda tetiklendiğinde. Çoklu
                // monitörde gerçek fayda sağladığı için döngü tamamen kaldırılmadı, ama tek monitörde
                // (en yaygın durum) sıfır fayda + kanıtlanmış çökme riski taşıdığından atlanıyor.
                IDXGIOutput? output = null;
                var expectedDeviceName = Screen.AllScreens.Length > 1
                    ? NexMote.Agent.Tray.ScreenCapture.GetDeviceNameForDisplayIndex(displayIndex)
                    : null;

                if (!string.IsNullOrEmpty(expectedDeviceName))
                {
                    try
                    {
                        for (int i = 0; i < 16; i++)
                        {
                            var res = adapter.EnumOutputs(i, out var candidate);
                            if (!res.Success || candidate is null)
                            {
                                break;
                            }

                            if (string.Equals(candidate.Description.DeviceName, expectedDeviceName, StringComparison.OrdinalIgnoreCase))
                            {
                                output = candidate;
                                break;
                            }

                            candidate.Dispose();
                        }
                    }
                    catch
                    {
                        // Aygıt adı eşleştirmesi herhangi bir nedenle patlarsa aşağıdaki kanıtlanmış
                        // index tabanlı yola sessizce düş.
                        output = null;
                    }
                }

                if (output is null)
                {
                    // Aygıt adı eşleşmesi bulunamadı (tek monitör, sürücü kısıtlaması vb.): eski index tabanlı yola düş
                    var enumRes = adapter.EnumOutputs(targetOutputIdx, out output);
                    if (!enumRes.Success || output is null)
                    {
                        enumRes = adapter.EnumOutputs(0, out output);
                        if (!enumRes.Success || output is null)
                        {
                            context.Dispose();
                            device.Dispose();
                            return null;
                        }
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

                    // Multi-monitor koordinat tespiti: gerçekte yakalanan DXGI çıktısının aygıt adına göre
                    // eşleşen Screen'i bul (index tabanlı değil — bkz. yukarıdaki açıklama).
                    var screens = Screen.AllScreens;
                    var matchedScreen = Array.Find(screens, s => string.Equals(s.DeviceName, output.Description.DeviceName, StringComparison.OrdinalIgnoreCase));
                    if (matchedScreen is not null)
                    {
                        bounds = matchedScreen.Bounds;
                    }
                    else if (targetOutputIdx < screens.Length)
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
