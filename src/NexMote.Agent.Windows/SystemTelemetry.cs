using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace NexMote.Agent.Windows;

/// <summary>
/// Hedef bilgisayardan RAM, Disk ve Fiziksel IPv4 donanım telemetrisini toplayan statik yardımcı sınıf.
/// </summary>
public static class SystemTelemetry
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Sanal ağ kartlarını (Hyper-V, WSL, VMware) filtreleyerek bilgisayarın gerçek fiziksel yerel IPv4 adresini tespit eder.
    /// </summary>
    public static string GetPrimaryIPv4Address()
    {
        try
        {
            // Dış ağ çıkış soketi simülasyonu ile varsayılan ağ geçidi üzerindeki IP'yi bul
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("1.1.1.1", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }

        try
        {
            // Soket başarısız olursa fiziksel ağ bağdaştırıcılarını tara
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                              !nic.Description.Contains("Virtual", StringComparison.OrdinalIgnoreCase) &&
                              !nic.Description.Contains("WSL", StringComparison.OrdinalIgnoreCase) &&
                              !nic.Description.Contains("Hyper-V", StringComparison.OrdinalIgnoreCase) &&
                              !nic.Description.Contains("VMware", StringComparison.OrdinalIgnoreCase))
                .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
                .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                             !IPAddress.IsLoopback(ip.Address) &&
                             !ip.Address.ToString().StartsWith("169.254"))
                .Select(ip => ip.Address.ToString())
                .FirstOrDefault() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    /// <summary>
    /// Win32 GlobalMemoryStatusEx çağrısı yaparak toplam ve kullanılan fiziksel RAM miktarını (MB) hesaplar.
    /// </summary>
    public static (long TotalMb, long UsedMb) GetMemoryMetrics()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(ref memStatus))
            {
                var totalMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                var usedMb = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024 * 1024));
                return (totalMb, usedMb);
            }
        }
        catch { }

        return (0, 0);
    }

    /// <summary>
    /// Sistem sürücüsündeki (C:\) kullanılabilir boş disk alanını (MB) döner.
    /// </summary>
    public static long GetDiskFreeMb()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(systemDrive);
            if (drive.IsReady)
            {
                return drive.AvailableFreeSpace / (1024 * 1024);
            }
        }
        catch { }

        return 0;
    }
}
