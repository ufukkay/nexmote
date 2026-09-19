using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace NexMote.TechnicianApp;

/// <summary>
/// Teknisyen masaüstü uygulamasının WPF başlangıç sınıfı.
/// DPI farkındalığını etkinleştirir, tek örnek (single-instance) çalışmayı garanti eder ve ana pencereyi başlatır.
/// </summary>
public partial class App : Application
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    // DİKKAT: "Global\" öneki KASITLI OLARAK kullanılmıyor — bu isimler böylece otomatik olarak
    // yalnızca çağıran oturuma (aynı kullanıcının masaüstü oturumu) özel kalır.
    private const string SingleInstanceMutexName = "NexMote_TechnicianApp_SingleInstance";
    private const string DeepLinkPipeName = "NexMote_TechnicianApp_DeepLink";

    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }

        base.OnStartup(e);

        var launchUri = e.Args.FirstOrDefault(a => a.StartsWith("nexmote://", StringComparison.OrdinalIgnoreCase));

        // Tek örnek (single-instance) koruması: web panelinden art arda "Bağlan"a basıldığında Windows
        // her seferinde nexmote:// protokolü için YENİ bir süreç başlatıyordu — hem birden fazla pencere
        // açılıyor hem de her yeni süreç kendi DPAPI oturum token dosyasını okuma/yazma sırasında öncekiyle
        // yarışa girip bozabildiğinden art arda şifre sorulmasına yol açıyordu. Artık ilk örnek bu Mutex'i
        // tutuyor; sonraki her başlatma isteği (yeni pencere AÇMADAN) mevcut, zaten kimlik doğrulanmış
        // örneğe adlandırılmış boru (named pipe) üzerinden iletilip süreç hemen kapanıyor.
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            TryForwardToExistingInstance(launchUri);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _mainWindow = new MainWindow();
        _mainWindow.Show();

        StartDeepLinkPipeServer();
    }

    private static void TryForwardToExistingInstance(string? launchUri)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", DeepLinkPipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(string.IsNullOrWhiteSpace(launchUri) ? "__activate__" : launchUri);
        }
        catch
        {
            // Mevcut örneğe ulaşılamadı (kilitlenmiş/kapanıyor olabilir) — sessizce geç,
            // kullanıcı gerekirse tekrar dener.
        }
    }

    private void StartDeepLinkPipeServer()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(DeepLinkPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync();
                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("nexmote://", StringComparison.OrdinalIgnoreCase))
                    {
                        _mainWindow?.HandleForwardedDeepLink(line);
                    }
                    else
                    {
                        _ = Dispatcher.BeginInvoke(() =>
                        {
                            if (_mainWindow is null) return;
                            if (_mainWindow.WindowState == WindowState.Minimized)
                            {
                                _mainWindow.WindowState = WindowState.Normal;
                            }
                            _mainWindow.Activate();
                        });
                    }
                }
                catch
                {
                    await Task.Delay(500);
                }
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_ownsMutex)
            {
                _singleInstanceMutex?.ReleaseMutex();
            }
            _singleInstanceMutex?.Dispose();
        }
        catch { }

        base.OnExit(e);
    }
}
