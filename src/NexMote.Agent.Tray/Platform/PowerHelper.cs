using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NexMote.Agent.Tray;

/// <summary>
/// Hedef bilgisayarı uzaktan kilitleme (LockWorkStation), yeniden başlatma veya kapatma komutlarını yürüten sınıf.
/// </summary>
internal static class PowerHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool LockWorkStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    public static void Execute(string action)
    {
        try
        {
            switch (action.ToLowerInvariant())
            {
                case "lock":
                    LockWorkStation();
                    break;
                case "logoff":
                    ExitWindowsEx(0x00000000 | 0x00000004, 0); // EWX_LOGOFF | EWX_FORCE
                    break;
                case "reboot":
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "reboot-safe":
                    Process.Start(new ProcessStartInfo("bcdedit.exe", "/set {current} safeboot network") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "reboot-normal":
                    Process.Start(new ProcessStartInfo("bcdedit.exe", "/deletevalue {current} safeboot") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(3000);
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
                case "shutdown":
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0 /f") { CreateNoWindow = true, UseShellExecute = false });
                    break;
            }
        }
        catch
        {
        }
    }
}
