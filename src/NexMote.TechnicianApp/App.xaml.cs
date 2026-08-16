using System;
using System.Runtime.InteropServices;
using System.Windows;

namespace NexMote.TechnicianApp;

/// <summary>
/// Teknisyen masaüstü uygulamasının WPF başlangıç sınıfı.
/// DPI farkındalığını etkinleştirir ve ana pencereyi (MainWindow) başlatır.
/// </summary>
public partial class App : Application
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch { }

        base.OnStartup(e);

        var window = new MainWindow();
        if (!window.CredentialsReady)
        {
            Shutdown();
            return;
        }

        window.Show();
    }
}
