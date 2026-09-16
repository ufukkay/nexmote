using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Kilit ekranı, Winlogon, kullanıcı değiştirme veya UAC durumlarında aktif interaktif masaüstüne (OpenWindowStation / OpenInputDesktop / SetThreadDesktop) bağlanmayı sağlayan Win32 köprüsü.
/// </summary>
internal static class DesktopHelper
{
    private const uint MAXIMUM_ALLOWED = 0x02000000;
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

    private const int UOI_NAME = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, [Out] byte[] pvInfo, uint nLength, out uint lpnLengthNeeded);

    [ThreadStatic]
    private static IntPtr _currentThreadDesktop;

    [ThreadStatic]
    private static string? _currentDesktopName;

    [ThreadStatic]
    private static long _lastDesktopCheckTicks;

    private static IntPtr _winsta0Handle = IntPtr.Zero;
    private static readonly object _winstaLock = new();

    public static string? GetDesktopName(IntPtr hDesktop)
    {
        if (hDesktop == IntPtr.Zero) return null;
        var buffer = new byte[512];
        if (GetUserObjectInformation(hDesktop, UOI_NAME, buffer, (uint)buffer.Length, out var lengthNeeded) && lengthNeeded > 0)
        {
            return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)lengthNeeded).TrimEnd('\0');
        }
        return null;
    }

    public static string? GetCurrentDesktopName() => _currentDesktopName;

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
    public static void AttachToActiveDesktop(bool force = false)
    {
        try
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            // Force değilse ve son 100ms içinde kontrol edildiyse gereksiz çağrıyı atla
            if (!force && _currentThreadDesktop != IntPtr.Zero && (now - _lastDesktopCheckTicks) < (System.Diagnostics.Stopwatch.Frequency / 10))
            {
                return;
            }

            _lastDesktopCheckTicks = now;
            EnsureWindowStation();

            // 1. Önce aktif girdi masaüstünü açmayı dene (Winlogon, Default veya UAC Secure Desktop)
            // DİKKAT: DESKTOP_ALL (0x020001FF) yerine MAXIMUM_ALLOWED (0x02000000) kullanılır,
            // çünkü Winlogon ACL kuralları journal playback bayraklarını reddeder.
            var hDesktop = OpenInputDesktop(0, false, MAXIMUM_ALLOWED);

            // 2. OpenInputDesktop başarısız olursa Winlogon masaüstünü doğrudan dene
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Winlogon", 0, false, MAXIMUM_ALLOWED);
            }

            // 3. Winlogon da açılamazsa Default masaüstünü dene
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Default", 0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop != IntPtr.Zero)
            {
                var newDesktopName = GetDesktopName(hDesktop);

                // Zaten aynı ada sahip masaüstüne bağlıysak mükerrer SetThreadDesktop çağrısı yapma
                if (!string.IsNullOrEmpty(newDesktopName) &&
                    string.Equals(newDesktopName, _currentDesktopName, StringComparison.OrdinalIgnoreCase) &&
                    _currentThreadDesktop != IntPtr.Zero)
                {
                    CloseDesktop(hDesktop);
                    return;
                }

                if (SetThreadDesktop(hDesktop))
                {
                    if (_currentThreadDesktop != IntPtr.Zero)
                    {
                        try { CloseDesktop(_currentThreadDesktop); } catch { }
                    }
                    _currentThreadDesktop = hDesktop;
                    _currentDesktopName = newDesktopName;
                }
                else
                {
                    CloseDesktop(hDesktop);
                }
            }
        }
        catch
        {
        }
    }
}
