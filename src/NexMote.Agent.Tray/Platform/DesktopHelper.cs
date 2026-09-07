using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Kilit ekranı, Winlogon, kullanıcı değiştirme veya UAC durumlarında aktif interaktif masaüstüne (OpenWindowStation / OpenInputDesktop / SetThreadDesktop) bağlanmayı sağlayan Win32 köprüsü.
/// </summary>
internal static class DesktopHelper
{
    private const uint GENERIC_ALL = 0x10000000;
    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint DESKTOP_ALL = 0x01FF | MAXIMUM_ALLOWED;
    private const uint WINSTA_ALL = 0x037F | MAXIMUM_ALLOWED;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenDesktop(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    [ThreadStatic]
    private static IntPtr _currentThreadDesktop;

    private static IntPtr _winsta0Handle = IntPtr.Zero;
    private static readonly object _winstaLock = new();

    /// <summary>
    /// Sürecin interaktif pencere istasyonuna (winsta0) bağlı olduğundan emin olur.
    /// </summary>
    public static void EnsureWindowStation()
    {
        try
        {
            if (_winsta0Handle == IntPtr.Zero)
            {
                lock (_winstaLock)
                {
                    if (_winsta0Handle == IntPtr.Zero)
                    {
                        var hWinSta = OpenWindowStation("winsta0", false, WINSTA_ALL);
                        if (hWinSta != IntPtr.Zero)
                        {
                            SetProcessWindowStation(hWinSta);
                            _winsta0Handle = hWinSta;
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Aktif masaüstüne (Default / Winlogon secure desktop) çağıran iş parçacığını iliştirir.
    /// Handle'ı hemen kapatmayıp iş parçacığı o masaüstünü kullandığı sürece açık tutar.
    /// </summary>
    public static void AttachToActiveDesktop()
    {
        try
        {
            EnsureWindowStation();

            var hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL);
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenInputDesktop(0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Winlogon", 0, false, DESKTOP_ALL);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Default", 0, false, DESKTOP_ALL);
            }

            if (hDesktop != IntPtr.Zero)
            {
                if (hDesktop != _currentThreadDesktop)
                {
                    if (SetThreadDesktop(hDesktop))
                    {
                        if (_currentThreadDesktop != IntPtr.Zero)
                        {
                            try { CloseDesktop(_currentThreadDesktop); } catch { }
                        }
                        _currentThreadDesktop = hDesktop;
                    }
                    else
                    {
                        CloseDesktop(hDesktop);
                    }
                }
                else
                {
                    // Zaten bu masaüstü handle'ına bağlıyız; açılan mükerrer handle'ı kapat
                    CloseDesktop(hDesktop);
                }
            }
        }
        catch
        {
        }
    }
}
