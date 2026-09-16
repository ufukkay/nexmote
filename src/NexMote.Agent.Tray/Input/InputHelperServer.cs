using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NexMote.Shared.Contracts;
using NexMote.Shared.Security;

namespace NexMote.Agent.Tray;

/// <summary>
/// SYSTEM yetkisinde çalışan ve Named Pipe ("NexMoteInputHelper_{SessionId}") üzerinden gelen girdi olaylarını dinleyerek
/// UIPI kısıtlamasını aşan ve UAC onay pencerelerine tıklama yapılmasını sağlayan yerel sunucu.
/// Kilit ekranı (Winlogon) ve UAC pencerelerinde de girdi enjeksiyonunu yürütür.
/// </summary>
internal static class InputHelperServer
{
    public static void Run()
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var mutexName = $@"Global\NexMoteInputHelperMutex_{sessionId}";
        
        Mutex? mutex = null;
        bool createdNew;
        try
        {
            var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var mutexSecurity = new MutexSecurity();
            mutexSecurity.AddAccessRule(new MutexAccessRule(worldSid, MutexRights.FullControl, AccessControlType.Allow));
            mutex = MutexAcl.Create(true, mutexName, out createdNew, mutexSecurity);
        }
        catch
        {
            mutex = new Mutex(true, mutexName, out createdNew);
        }

        if (!createdNew)
        {
            mutex?.Dispose();
            return;
        }

        var pipeName = $"NexMoteInputHelper_{sessionId}";
        var security = BuildPipeSecurity();

        DesktopHelper.EnsureWindowStation();

        while (true)
        {
            try
            {
                var server = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 4096,
                    outBufferSize: 4096,
                    security);

                server.WaitForConnection();

                // Her bağlanan istemciyi (Tray canlı girdi veya Servis SAS sinyali) arka planda
                // bağımsız bir iş parçacığında işle; böylece bir istemci boruyu açık tutarken diğerleri engellenmez.
                _ = Task.Run(() => ProcessClient(server));
            }
            catch
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void ProcessClient(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                if (!IsAllowedClient(server))
                {
                    server.Disconnect();
                    return;
                }

                DesktopHelper.EnsureWindowStation();

                using var reader = new StreamReader(server, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    HandleCommand(line);
                }
            }
            catch
            {
            }
        }
    }

    private static void HandleCommand(string json)
    {
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var input = JsonSerializer.Deserialize<RemoteInputEvent>(json, options);
            if (input is null)
            {
                return;
            }

            switch (input.Kind.ToLowerInvariant())
            {
                case "mouse-move":
                    InputInjector.MoveMouse(input.DisplayIndex, input.X, input.Y);
                    break;
                case "mouse-button":
                    InputInjector.MoveMouse(input.DisplayIndex, input.X, input.Y);
                    InputInjector.MouseButton(input.Button, input.IsDown);
                    break;
                case "mouse-wheel":
                    InputInjector.MouseWheel(input.WheelDelta);
                    break;
                case "key":
                    InputInjector.Keyboard(input.KeyCode, input.IsDown);
                    break;
                case "send-sas":
                    SasHelper.SendSas();
                    break;
                case "power-action":
                    PowerHelper.Execute(input.Button ?? "lock");
                    break;
            }
        }
        catch
        {
        }
    }

    private static bool IsAllowedClient(NamedPipeServerStream server)
    {
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid))
            {
                return false;
            }

            // 1. İstemcinin aynı Windows Oturumunda (SessionId) veya Session 0 (Windows Servisi) çalıştığını doğrula
            var currentSessionId = Process.GetCurrentProcess().SessionId;
            try
            {
                using var clientProc = Process.GetProcessById((int)pid);
                // Aynı oturumdaki Tray uygulamasına VEYA Session 0'da çalışan Windows Servisine (Worker.cs SAS sinyali için) izin ver
                if (clientProc.SessionId != currentSessionId && clientProc.SessionId != 0)
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (int)pid);
            if (hProcess == IntPtr.Zero)
            {
                return false;
            }

            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (!QueryFullProcessImageName(hProcess, 0, sb, ref size))
            {
                return false;
            }

            // 2. İstemci dosya yolunun kendi yürütülebilir dosyamızla veya Windows Servisiyle eşleştiğini doğrula
            var clientPath = sb.ToString();
            var selfPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(clientPath) || string.IsNullOrEmpty(selfPath))
            {
                return false;
            }

            var clientFileName = Path.GetFileName(clientPath);
            if (string.Equals(clientPath, selfPath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clientFileName, "NexMote.Agent.Tray.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(clientFileName, "NexMote.Agent.Windows.exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var verification = AuthenticodeVerifier.Verify(clientPath, expectedSubjectContains: "NexMote", allowUntrustedRootInDev: true);
            return verification.IsValid;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                CloseHandle(hProcess);
            }
        }
    }

    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        // Domain veya yerel oturum açmış kullanıcıların (Tray süreci) Named Pipe'a yazabilmesine izin ver
        var authUserSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        security.AddAccessRule(new PipeAccessRule(authUserSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        security.AddAccessRule(new PipeAccessRule(worldSid, PipeAccessRights.ReadWrite, AccessControlType.Allow));

        return security;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}
