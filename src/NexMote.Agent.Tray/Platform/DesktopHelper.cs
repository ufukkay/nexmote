using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Kilit ekranı, Winlogon, kullanıcı değiştirme veya UAC durumlarında aktif interaktif masaüstüne (OpenWindowStation / OpenInputDesktop / SetThreadDesktop) bağlanmayı sağlayan Win32 köprüsü.
/// </summary>
internal static class DesktopHelper
{
    private const uint GENERIC_ALL = 0x10000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenWindowStation(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    private const uint MAXIMUM_ALLOWED = 0x02000000;
    private const uint DESKTOP_ALL = 0x01FF | MAXIMUM_ALLOWED;

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

    /// <summary>
    /// Aktif masaüstüne (Default / Winlogon secure desktop) çağıran iş parçacığını iliştirir.
    /// </summary>
    public static void AttachToActiveDesktop()
    {
        try
        {
            var hDesktop = OpenInputDesktop(0, false, DESKTOP_ALL);
            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenInputDesktop(0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Winlogon", 0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop == IntPtr.Zero)
            {
                hDesktop = OpenDesktop("Default", 0, false, MAXIMUM_ALLOWED);
            }

            if (hDesktop != IntPtr.Zero)
            {
                try
                {
                    SetThreadDesktop(hDesktop);
                }
                finally
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
