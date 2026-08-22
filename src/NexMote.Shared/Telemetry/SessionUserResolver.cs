using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NexMote.Shared.Telemetry;

/// <summary>
/// Aktif konsol oturumundaki (fiziksel ekran başındaki) gerçek kullanıcı adını WTS API'leri üzerinden çözen
/// paylaşımlı çözümleyici. Hem SYSTEM yetkili Windows Servisi (NexMote.Agent.Windows) hem de kullanıcı
/// oturumundaki Tray süreci (NexMote.Agent.Tray) tarafından ORTAK kullanılır — böylece sunucuya iletilen
/// ActiveUser alanı, heartbeat'i hangi sürecin attığından bağımsız olarak her zaman aynı, tutarlı biçimde
/// (domain önekisiz, temiz) gönderilir.
/// </summary>
public static class SessionUserResolver
{
    /// <summary>
    /// Fiziksel konsolda (ekran başında) aktif olan interaktif Windows oturum numarasını (Session ID) döner.
    /// </summary>
    public static uint GetActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    /// <summary>
    /// Kullanıcı adından domain prefix (DOMAIN\) veya UPN suffix (@domain) kısımlarını temizler.
    /// Sistem ve makine hesaplarını (SYSTEM, $, DWM vb.) eler.
    /// </summary>
    public static string CleanUserName(string? rawUser)
    {
        if (string.IsNullOrWhiteSpace(rawUser)) return string.Empty;

        var user = rawUser.Trim();

        // 1. "DOMAIN\username" veya "domain.local\username" -> "username"
        var backslashIdx = user.LastIndexOf('\\');
        if (backslashIdx >= 0 && backslashIdx < user.Length - 1)
        {
            user = user.Substring(backslashIdx + 1);
        }

        // 2. "username@domain.local" -> "username"
        var atIdx = user.IndexOf('@');
        if (atIdx > 0)
        {
            user = user.Substring(0, atIdx);
        }

        user = user.Trim();

        // Sistem hesapları veya makine hesabı ($ ile biten) ise yoksay
        if (user.EndsWith("$", StringComparison.Ordinal) ||
            string.Equals(user, "SYSTEM", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user, "LOCAL SERVICE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(user, "NETWORK SERVICE", StringComparison.OrdinalIgnoreCase) ||
            user.StartsWith("DWM-", StringComparison.OrdinalIgnoreCase) ||
            user.StartsWith("UMFD-", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return user;
    }

    /// <summary>
    /// Windows Kayıt Defterinden bilgisayarda son oturum açan kullanıcının temiz adını sorgular.
    /// </summary>
    public static string? GetLastLoggedOnUserFromRegistry()
    {
        try
        {
            using var logonUi = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI");
            if (logonUi != null)
            {
                var val = (logonUi.GetValue("LastLoggedOnSAMUser") as string)
                       ?? (logonUi.GetValue("LastLoggedOnUser") as string)
                       ?? (logonUi.GetValue("LastLoggedOnDisplayName") as string);

                var cleaned = CleanUserName(val);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    return cleaned;
                }
            }

            using var winlogon = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
            if (winlogon != null)
            {
                var val = (winlogon.GetValue("DefaultUserName") as string)
                       ?? (winlogon.GetValue("LastUsedUsername") as string)
                       ?? (winlogon.GetValue("AltDefaultUserName") as string);

                var cleaned = CleanUserName(val);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    return cleaned;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// Fiziksel konsolda aktif olan interaktif oturumda gerçekten oturum açmış kullanıcının
    /// veya kilit ekranındaysa son oturum açan kullanıcının temiz adını (Domain olmaksızın) döner.
    /// </summary>
    public static string GetActiveSessionUserName()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId != 0xFFFFFFFF)
        {
            if (TryQuerySessionInfoString(sessionId, WTS_INFO_CLASS.WTSUserName, out var userName) && !string.IsNullOrWhiteSpace(userName))
            {
                var cleaned = CleanUserName(userName);
                if (!string.IsNullOrEmpty(cleaned))
                {
                    return cleaned;
                }
            }
        }

        // Aktif oturum yoksa veya kilit ekranıysa son oturum açan kullanıcıyı al
        var lastUser = GetLastLoggedOnUserFromRegistry();
        if (!string.IsNullOrEmpty(lastUser))
        {
            return lastUser;
        }

        return CleanUserName(Environment.UserName);
    }

    private static bool TryQuerySessionInfoString(uint sessionId, WTS_INFO_CLASS infoClass, out string value)
    {
        value = string.Empty;
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out _))
        {
            return false;
        }

        try
        {
            value = Marshal.PtrToStringUni(buffer) ?? string.Empty;
            return true;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private enum WTS_INFO_CLASS
    {
        WTSUserName = 5,
        WTSDomainName = 7
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "WTSQuerySessionInformationW")]
    private static extern bool WTSQuerySessionInformation(IntPtr hServer, uint sessionId, WTS_INFO_CLASS wtsInfoClass, out IntPtr ppBuffer, out uint pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pMemory);
}
