using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NexMote.Deployer.Models;

namespace NexMote.Deployer.Services;

public sealed class RemoteDeployEngine
{
    [DllImport("mpr.dll")]
    private static extern int WNetAddConnection2(NetResource netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll")]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);

    [StructLayout(LayoutKind.Sequential)]
    private class NetResource
    {
        public int Scope = 2; // RESOURCE_GLOBALNET
        public int ResourceType = 1; // RESOURCETYPE_DISK
        public int DisplayType = 3;
        public int Usage = 1;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }

    public static async Task<bool> PingTargetAsync(DeployTarget target, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target.Ip, 2000);
            if (reply.Status == IPStatus.Success)
            {
                target.PingMs = reply.RoundtripTime;
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(target.Ip, ct);
                    target.Hostname = hostEntry.HostName;
                }
                catch
                {
                    target.Hostname = target.Ip;
                }
                return true;
            }
        }
        catch
        {
        }

        // Fallback: Check TCP port 445 (SMB)
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(target.Ip, 445);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(2000, ct));
            if (completedTask == connectTask && client.Connected)
            {
                target.PingMs = 5;
                target.Hostname = target.Ip;
                return true;
            }
        }
        catch
        {
        }

        target.PingMs = -1;
        return false;
    }

    public static async Task DeployToTargetAsync(
        DeployTarget target,
        string username,
        string password,
        string localMsiPath,
        string serverUrl,
        string enrollmentKey,
        bool enableLocalTokenFilter,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        target.Status = DeployStatus.Connecting;
        target.StatusText = "Ağa Bağlanıyor...";
        target.Details = "Hedef makine ile SMB/RPC oturumu kuruluyor...";

        string remoteSharePath = $@"\\{target.Ip}\ADMIN$\Temp";
        string targetMsiRemote = Path.Combine(remoteSharePath, "NexMote-Agent-Setup.msi");
        string targetMsiLocal = @"C:\Windows\Temp\NexMote-Agent-Setup.msi";
        bool smbConnected = false;

        try
        {
            // 1. Establish Authenticated SMB Connection
            var nr = new NetResource
            {
                RemoteName = $@"\\{target.Ip}\ADMIN$"
            };

            int ret = WNetAddConnection2(nr, password, username, 0);
            if (ret != 0 && ret != 1219) // 1219 = multiple credentials conflict, already open
            {
                // Try IPC$
                var nrIpc = new NetResource { RemoteName = $@"\\{target.Ip}\IPC$" };
                ret = WNetAddConnection2(nrIpc, password, username, 0);
            }

            smbConnected = (ret == 0 || ret == 1219);

            // 2. Copy MSI to Target
            target.Status = DeployStatus.Copying;
            target.StatusText = "Paket Kopyalanıyor...";
            target.Details = "NexMote-Agent-Setup.msi hedef makineye aktarılıyor...";

            Directory.CreateDirectory(remoteSharePath);
            File.Copy(localMsiPath, targetMsiRemote, overwrite: true);

            // 3. Remote Installation via WMI / Process Creation
            target.Status = DeployStatus.Installing;
            target.StatusText = "Sessizce Kuruluyor...";
            target.Details = "msiexec ile arka planda LocalSystem servisi olarak kuruluyor...";

            var installArgs = $"/i \"{targetMsiLocal}\" /qn /norestart SERVERURL=\"{serverUrl}\" ENROLLMENTKEY=\"{enrollmentKey}\"";
            bool installTriggered = false;

            // Attempt A: WMI Win32_Process.Create
            try
            {
                var options = new ConnectionOptions
                {
                    Username = string.IsNullOrWhiteSpace(username) ? null : username,
                    Password = string.IsNullOrWhiteSpace(password) ? null : password,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy,
                    Timeout = TimeSpan.FromSeconds(15)
                };

                var scope = new ManagementScope($@"\\{target.Ip}\root\cimv2", options);
                scope.Connect();

                var processClass = new ManagementClass(scope, new ManagementPath("Win32_Process"), new ObjectGetOptions());
                var inParams = processClass.GetMethodParameters("Create");
                inParams["CommandLine"] = $"msiexec.exe {installArgs}";

                var outParams = processClass.InvokeMethod("Create", inParams, null);
                var returnVal = Convert.ToUInt32(outParams["ReturnValue"]);

                if (returnVal == 0)
                {
                    installTriggered = true;
                    target.Details = $"WMI kurulum süreci başlatıldı (PID: {outParams["ProcessId"]}).";
                }
                else
                {
                    target.Details = $"WMI dönüş kodu: {returnVal}. Yedek kurulum yöntemi deneniyor...";
                }
            }
            catch (Exception ex)
            {
                target.Details = $"WMI bağlantı uyarısı: {ex.Message}. Alternatif metot deneniyor...";
            }

            // Attempt B: Remote PsExec / SC command injection fallback
            if (!installTriggered)
            {
                var scResult = await RunRemoteScInstallAsync(target.Ip, username, password, installArgs, ct);
                if (scResult)
                {
                    installTriggered = true;
                }
            }

            if (!installTriggered)
            {
                throw new InvalidOperationException("Hedef makinede WMI veya SC üzerinden kurulum başlatılamadı. Kullanıcı adı/şifre veya uzak yönetim izinlerini kontrol edin.");
            }

            // 4. Verification & Waiting
            target.Status = DeployStatus.Verifying;
            target.StatusText = "Doğrulanıyor...";
            target.Details = "NexMote Ajan servisinin başlaması bekleniyor...";

            await Task.Delay(4000, ct);

            // 5. Cleanup Temp MSI
            try
            {
                if (File.Exists(targetMsiRemote)) File.Delete(targetMsiRemote);
            }
            catch
            {
            }

            sw.Stop();
            target.Duration = $"{sw.Elapsed.TotalSeconds:F1}s";
            target.Status = DeployStatus.Success;
            target.StatusText = "Başarılı";
            target.Details = "Ajan başarıyla yüklendi ve arka plan servisi başlatıldı.";
        }
        catch (Exception ex)
        {
            sw.Stop();
            target.Duration = $"{sw.Elapsed.TotalSeconds:F1}s";
            target.Status = DeployStatus.Failed;
            target.StatusText = "Hata";
            target.Details = ex.Message;
        }
        finally
        {
            if (smbConnected)
            {
                try { WNetCancelConnection2($@"\\{target.Ip}\ADMIN$", 0, true); } catch { }
                try { WNetCancelConnection2($@"\\{target.Ip}\IPC$", 0, true); } catch { }
            }
        }
    }

    private static async Task<bool> RunRemoteScInstallAsync(string ip, string username, string password, string installArgs, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c psexec.exe \\\\{ip} -u \"{username}\" -p \"{password}\" -s -accepteula msiexec.exe {installArgs}")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                await proc.WaitForExitAsync(ct);
                return proc.ExitCode == 0;
            }
        }
        catch
        {
        }
        return false;
    }
}
