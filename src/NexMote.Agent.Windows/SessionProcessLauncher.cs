using System.Runtime.InteropServices;

namespace NexMote.Agent.Windows;

/// <summary>
/// Windows Servisi (LocalSystem) içerisinden aktif kullanıcı masaüstü oturumuna (Session 1, 2 vb.)
/// SYSTEM yetkisine sahip bir süreç (NexMote.Agent.Tray.exe --input-helper) başlatan Win32 API köprüsü.
/// 
/// Neden Gerekli (UIPI & UAC İzolasyonu):
/// Standart kullanıcı yetkisindeki bir süreçten gönderilen fare/klavye girdileri (SendInput),
/// Windows UIPI (User Interface Privilege Isolation) güvenlik katmanı nedeniyle daha yüksek bütünlükteki
/// UAC (Kullanıcı Hesabı Denetimi) onay pencerelerine ve parola alanlarına tıklayamaz.
/// Bu sınıf sayesinde başlatılan yardımcı süreç SYSTEM yetkisinde çalıştığından, teknisyenin uzaktan
/// gönderdiği tıklamalar UAC onay pencerelerine sorunsuz şekilde etki edebilir.
/// </summary>
internal static class SessionProcessLauncher
{
    /// <summary>
    /// Fiziksel konsolda (ekran başında) aktif olan interaktif Windows oturum numarasını (Session ID) döner.
    /// </summary>
    public static uint GetActiveConsoleSessionId() => WTSGetActiveConsoleSessionId();

    /// <summary>
    /// Belirtilen oturum içerisinde hedef isimli sürecin çalışıp çalışmadığını kontrol eder.
    /// </summary>
    public static bool IsProcessRunningInSession(string processName, uint sessionId)
    {
        try
        {
            var procs = System.Diagnostics.Process.GetProcessesByName(processName);
            foreach (var p in procs)
            {
                if (p.SessionId == (int)sessionId)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    /// <summary>
    /// LocalSystem belirtecini çoğaltıp aktif oturuma bağlayarak (TokenSessionId + SeTcbPrivilege)
    /// belirtilen yürütülebilir dosyayı o oturumun masaüstünde başlatır.
    /// </summary>
    /// <param name="exePath">Çalıştırılacak dosya yolu.</param>
    /// <param name="arguments">Komut satırı argümanları (örn: --input-helper veya --tray).</param>
    /// <param name="error">Hata oluşması durumunda açıklama metni.</param>
    public static bool TryLaunchInActiveSession(string exePath, string arguments, out string error)
    {
        error = string.Empty;
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            error = "Aktif interaktif konsol oturumu bulunamadı.";
            return false;
        }

        // Mevcut servis sürecinin belirtecini (SYSTEM token) aç
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_DUPLICATE | TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var processToken))
        {
            error = $"OpenProcessToken başarısız: {Marshal.GetLastWin32Error()}";
            return false;
        }

        try
        {
            // TCB (Trusted Computer Base) ayrıcalığını etkinleştir
            if (!EnableTcbPrivilege(processToken, out error))
            {
                return false;
            }

            // Birincil yürütme belirtecini (Primary Token) çoğalt
            if (!DuplicateTokenEx(processToken, TOKEN_ALL_ACCESS, IntPtr.Zero, SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var sessionToken))
            {
                error = $"DuplicateTokenEx başarısız: {Marshal.GetLastWin32Error()}";
                return false;
            }

            try
            {
                // Çoğaltılan belirtecin hedef oturum kimliğini aktif oturum ID'sine ayarla
                var sid = sessionId;
                if (!SetTokenInformation(sessionToken, TOKEN_INFORMATION_CLASS.TokenSessionId, ref sid, sizeof(uint)))
                {
                    error = $"SetTokenInformation(TokenSessionId) başarısız: {Marshal.GetLastWin32Error()}";
                    return false;
                }

                var hasEnv = CreateEnvironmentBlock(out var envBlock, sessionToken, false);

                try
                {
                    var si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf<STARTUPINFO>();
                    si.lpDesktop = @"winsta0\default"; // Kullanıcının gördüğü varsayılan interaktif masaüstü

                    const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
                    const int CREATE_NO_WINDOW = 0x08000000;

                    var commandLine = $"\"{exePath}\" {arguments}";
                    var workingDir = Path.GetDirectoryName(exePath);

                    // Hedef oturum içinde SYSTEM yetkisinde süreci başlat (önce default, başarısızsa Winlogon masaüstü)
                    var created = CreateProcessAsUser(
                        sessionToken,
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                        hasEnv ? envBlock : IntPtr.Zero,
                        workingDir,
                        ref si,
                        out var pi);

                    if (!created)
                    {
                        // Kullanıcı henüz giriş yapmamışsa (Windows Giriş/Kilit Ekranı) winsta0\Winlogon masaüstünü dene
                        si.lpDesktop = @"winsta0\Winlogon";
                        created = CreateProcessAsUser(
                            sessionToken,
                            null,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            CREATE_UNICODE_ENVIRONMENT | CREATE_NO_WINDOW,
                            hasEnv ? envBlock : IntPtr.Zero,
                            workingDir,
                            ref si,
                            out pi);
                    }

                    if (!created)
                    {
                        error = $"CreateProcessAsUser başarısız: {Marshal.GetLastWin32Error()}";
                        return false;
                    }

                    CloseHandle(pi.hProcess);
                    CloseHandle(pi.hThread);
                    return true;
                }
                finally
                {
                    if (hasEnv)
                    {
                        DestroyEnvironmentBlock(envBlock);
                    }
                }
            }
            finally
            {
                CloseHandle(sessionToken);
            }
        }
        finally
        {
            CloseHandle(processToken);
        }
    }

    /// <summary>
    /// Süreç belirteci üzerinde SeTcbPrivilege (Act as part of the operating system) ayrıcalığını etkinleştirir.
    /// </summary>
    private static bool EnableTcbPrivilege(IntPtr tokenHandle, out string error)
    {
        error = string.Empty;
        if (!LookupPrivilegeValue(null, "SeTcbPrivilege", out var luid))
        {
            error = $"LookupPrivilegeValue başarısız: {Marshal.GetLastWin32Error()}";
            return false;
        }

        var tp = new TOKEN_PRIVILEGES
        {
            PrivilegeCount = 1,
            Luid = luid,
            Attributes = SE_PRIVILEGE_ENABLED
        };

        if (!AdjustTokenPrivileges(tokenHandle, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
        {
            error = $"AdjustTokenPrivileges başarısız: {Marshal.GetLastWin32Error()}";
            return false;
        }

        var lastError = Marshal.GetLastWin32Error();
        if (lastError == ERROR_NOT_ALL_ASSIGNED)
        {
            error = "SeTcbPrivilege ayrıcalığı belirteçte tanımlı değil (Servis LocalSystem olarak çalışmalıdır).";
            return false;
        }

        return true;
    }

    private const uint TOKEN_DUPLICATE = 0x0002;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint TOKEN_ALL_ACCESS = 0xF01FF;
    private const uint SE_PRIVILEGE_ENABLED = 0x00000002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    private enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    private enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenSessionId = 12
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetTokenInformation(IntPtr tokenHandle, TOKEN_INFORMATION_CLASS tokenInformationClass, ref uint tokenInformation, uint tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
