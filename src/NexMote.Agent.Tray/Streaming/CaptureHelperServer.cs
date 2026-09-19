using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace NexMote.Agent.Tray;

/// <summary>
/// Ekran yakalamayı (DXGI/GDI+) ana Tray sürecinden İZOLE bir alt süreçte çalıştıran adlandırılmış boru
/// (named pipe) sunucusu. Bazı GPU sürücüleri (üretimde kanıtlandı: Intel Iris Xe) DXGI Desktop Duplication
/// çağrılarında native bir erişim ihlaliyle (AccessViolationException, .NET try/catch ile YAKALANAMAZ) tüm
/// süreci çökertebiliyor. Yakalamayı bu ayrı, gözetlenen (<see cref="CaptureHelperClient"/>) alt sürece
/// taşıyarak, böyle bir çökme yalnızca bu alt süreci öldürür — ana Tray süreci (SignalR/WebRTC/girdi
/// yönlendirme) hayatta kalır ve görüntü akışı kısa bir donmadan sonra kendi kendini toparlar.
/// </summary>
internal static class CaptureHelperServer
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static void Run(string[] args)
    {
        var pipeName = GetArg(args, "--pipe");
        var parentPidText = GetArg(args, "--parent-pid");
        if (string.IsNullOrEmpty(pipeName) || !int.TryParse(parentPidText, out var parentPid))
        {
            return;
        }

        var mutexName = $@"Global\NexMoteCaptureHelperMutex_{parentPid}";
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

        // Ebeveyn (Tray) süreci öldüyse (örn. çöktü ve yenisi başka bir pipe adıyla başladı) bu süreç
        // yetim kalmasın diye kendini kapatır.
        var watchdogThread = new Thread(() => WatchParentLiveness(parentPid)) { IsBackground = true };
        watchdogThread.Start();

        DesktopHelper.EnsureWindowStation();

        var security = BuildPipeSecurity();

        while (true)
        {
            try
            {
                var server = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 8,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 1024 * 1024,
                    outBufferSize: 1024 * 1024,
                    security);

                server.WaitForConnection();

                // Her bağlantı (bir ekran/display'e karşılık gelir) kendi özel thread'inde, senkron
                // istek/yanıt döngüsüyle işlenir — böylece bir ekranın yakalaması diğerini bloklamaz.
                var thread = new Thread(() => ProcessDisplayConnection(server)) { IsBackground = true };
                thread.Start();
            }
            catch
            {
                Thread.Sleep(250);
            }
        }
    }

    private static void WatchParentLiveness(int parentPid)
    {
        while (true)
        {
            try
            {
                using var proc = Process.GetProcessById(parentPid);
                if (proc.HasExited)
                {
                    Environment.Exit(0);
                }
            }
            catch
            {
                Environment.Exit(0);
            }

            Thread.Sleep(3000);
        }
    }

    private static void ProcessDisplayConnection(NamedPipeServerStream server)
    {
        using (server)
        {
            try
            {
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                using var writer = new StreamWriter(server, Encoding.UTF8, 1024 * 1024, leaveOpen: true) { AutoFlush = true };

                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    writer.WriteLine(HandleRequest(line));
                }
            }
            catch
            {
                // Bağlantı koptu (Tray tarafı yeniden bağlanacak) — sessizce bu thread'i sonlandır.
            }
        }
    }

    private static string HandleRequest(string line)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CaptureRequest>(line, JsonOptions);
            if (request is null)
            {
                return "{}";
            }

            if (request.ResetHash)
            {
                try { ScreenCapture.ResetHash(request.DisplayIndex); } catch { }
            }

            string? jpeg;
            try
            {
                jpeg = ScreenCapture.CaptureJpegBase64(request.DisplayIndex, request.Quality, request.ForceSend);
            }
            catch
            {
                jpeg = null;
            }

            return JsonSerializer.Serialize(new CaptureResponse(jpeg), JsonOptions);
        }
        catch
        {
            return "{}";
        }
    }

    private static string? GetArg(string[] args, string name)
    {
        var prefix = name + "=";
        foreach (var a in args)
        {
            if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return a[prefix.Length..];
            }
        }
        return null;
    }

    private static PipeSecurity BuildPipeSecurity()
    {
        var security = new PipeSecurity();
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        security.AddAccessRule(new PipeAccessRule(systemSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        var adminSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.AddAccessRule(new PipeAccessRule(adminSid, PipeAccessRights.FullControl, AccessControlType.Allow));

        return security;
    }
}

internal sealed record CaptureRequest(int DisplayIndex, int Quality, bool ForceSend, bool ResetHash);

internal sealed record CaptureResponse(string? JpegBase64);
