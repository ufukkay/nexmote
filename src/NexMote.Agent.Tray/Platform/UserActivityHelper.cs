using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray.Platform;

/// <summary>
/// Kullanıcının klavye ve fare aktivitesini Win32 GetLastInputInfo ile izleyerek boşta kalma süresini belirler.
/// </summary>
internal static class UserActivityHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    public static TimeSpan GetIdleDuration()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (GetLastInputInfo(ref lii))
        {
            uint tickCount = (uint)Environment.TickCount;
            return TimeSpan.FromMilliseconds(tickCount - lii.dwTime);
        }
        return TimeSpan.Zero;
    }

    public static bool IsUserIdle(int idleMinutesThreshold)
    {
        if (idleMinutesThreshold <= 0) return false;
        return GetIdleDuration() >= TimeSpan.FromMinutes(idleMinutesThreshold);
    }
}
