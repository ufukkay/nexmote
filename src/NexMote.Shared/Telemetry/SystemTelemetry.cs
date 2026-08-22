using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NexMote.Shared.Contracts;

namespace NexMote.Shared.Telemetry;

/// <summary>
/// Arka planda Win32 GetSystemTimes API'sini kullanarak 15 saniyede bir gerçek CPU kullanımını örnekleyen
/// ve 10 dakikalık kayan pencere (rolling window) ortalamasını hesaplayan telemetri toplayıcı.
/// </summary>
public sealed class CpuUsageSampler : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan WindowSize = TimeSpan.FromMinutes(10);

    private readonly object _lock = new();
    private readonly List<(DateTimeOffset Timestamp, double Percent)> _samples = new();
    private readonly Timer _timer;

    private long _lastIdle;
    private long _lastKernel;
    private long _lastUser;
    private bool _hasBaseline;

    public CpuUsageSampler()
    {
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, SampleInterval);
    }

    /// <summary>
    /// Son 10 dakika içindeki CPU kullanım örneklerinin yuvarlanmış ortalama yüzdesini (0-100) döner.
    /// </summary>
    public int GetAveragePercent()
    {
        lock (_lock)
        {
            if (_samples.Count == 0)
            {
                return 0;
            }

            return (int)Math.Round(_samples.Average(s => s.Percent));
        }
    }

    /// <summary>
    /// Win32 GetSystemTimes çağrısı yaparak çekirdek, kullanıcı ve boşta kalma süreleri arasındaki farktan CPU yükünü hesaplar.
    /// </summary>
    private void Sample()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        if (!GetSystemTimes(out var idle, out var kernel, out var user))
        {
            return;
        }

        var idleTicks = ToInt64(idle);
        var kernelTicks = ToInt64(kernel);
        var userTicks = ToInt64(user);

        lock (_lock)
        {
            if (!_hasBaseline)
            {
                _lastIdle = idleTicks;
                _lastKernel = kernelTicks;
                _lastUser = userTicks;
                _hasBaseline = true;
                return;
            }

            var idleDelta = idleTicks - _lastIdle;
            var kernelDelta = kernelTicks - _lastKernel;
            var userDelta = userTicks - _lastUser;
            _lastIdle = idleTicks;
            _lastKernel = kernelTicks;
            _lastUser = userTicks;

            var totalDelta = kernelDelta + userDelta;
            if (totalDelta <= 0)
            {
                return;
            }

            var busyDelta = totalDelta - idleDelta;
            var percent = Math.Clamp(busyDelta * 100.0 / totalDelta, 0, 100);

            var now = DateTimeOffset.UtcNow;
            _samples.Add((now, percent));
            _samples.RemoveAll(s => now - s.Timestamp > WindowSize);
        }
    }

    public void Dispose() => _timer.Dispose();

    private static long ToInt64(FILETIME ft) => ((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);
}

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
    /// Microsoft Windows NT çekirdek dizesi (10.0.26200.0) yerine kullanıcı dostu Windows sürüm ve derleme adını döner.
    /// Örn: "Windows 11 Pro (24H2) [26100.3194]", "Windows 10 Pro (22H2)"
    /// </summary>
    public static string GetFriendlyOperatingSystemName()
    {
        if (!OperatingSystem.IsWindows())
        {
            return RuntimeInformation.OSDescription;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null)
            {
                var productName = (key.GetValue("ProductName") as string)?.Trim() ?? "Windows";
                var displayVersion = (key.GetValue("DisplayVersion") as string)?.Trim();
                if (string.IsNullOrEmpty(displayVersion))
                {
                    displayVersion = (key.GetValue("ReleaseId") as string)?.Trim();
                }

                var currentBuildStr = (key.GetValue("CurrentBuild") as string)?.Trim() ?? (key.GetValue("CurrentBuildNumber") as string)?.Trim();
                int.TryParse(currentBuildStr, out var currentBuild);

                var ubr = key.GetValue("UBR");
                var buildWithUbr = currentBuild > 0 
                    ? (ubr != null ? $"{currentBuild}.{ubr}" : currentBuild.ToString()) 
                    : "";

                var editionId = (key.GetValue("EditionID") as string)?.Trim();
                var installationType = (key.GetValue("InstallationType") as string)?.Trim();

                var isServer = installationType?.Contains("Server", StringComparison.OrdinalIgnoreCase) == true ||
                               productName.Contains("Server", StringComparison.OrdinalIgnoreCase);

                string osFamily;
                if (isServer)
                {
                    if (currentBuild >= 26100) osFamily = "Windows Server 2025";
                    else if (currentBuild >= 20348) osFamily = "Windows Server 2022";
                    else if (currentBuild >= 17763) osFamily = "Windows Server 2019";
                    else if (currentBuild >= 14393) osFamily = "Windows Server 2016";
                    else osFamily = productName;
                }
                else
                {
                    if (currentBuild >= 22000)
                    {
                        var edition = !string.IsNullOrEmpty(editionId) 
                            ? (editionId.Equals("Professional", StringComparison.OrdinalIgnoreCase) ? "Pro" : editionId)
                            : (productName.Contains("Pro", StringComparison.OrdinalIgnoreCase) ? "Pro" : 
                               productName.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) ? "Enterprise" : "Home");
                        osFamily = $"Windows 11 {edition}";
                    }
                    else if (currentBuild >= 10240)
                    {
                        var edition = !string.IsNullOrEmpty(editionId) 
                            ? (editionId.Equals("Professional", StringComparison.OrdinalIgnoreCase) ? "Pro" : editionId)
                            : (productName.Contains("Pro", StringComparison.OrdinalIgnoreCase) ? "Pro" : "Home");
                        osFamily = $"Windows 10 {edition}";
                    }
                    else
                    {
                        osFamily = productName;
                    }
                }

                var versionPart = !string.IsNullOrEmpty(displayVersion) ? $" ({displayVersion})" : "";
                var buildPart = !string.IsNullOrEmpty(buildWithUbr) ? $" [Derleme {buildWithUbr}]" : "";

                return $"{osFamily}{versionPart}{buildPart}".Trim();
            }
        }
        catch { }

        return Environment.OSVersion.VersionString;
    }

    /// <summary>
    /// Sanal ağ kartlarını (Hyper-V, WSL, VMware) filtreleyerek bilgisayarın gerçek fiziksel yerel IPv4 adresini tespit eder.
    /// </summary>
    public static string GetPrimaryIPv4Address()
    {
        try
        {
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
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var memStatus = new MEMORYSTATUSEX();
                if (GlobalMemoryStatusEx(ref memStatus))
                {
                    var totalMb = (long)(memStatus.ullTotalPhys / (1024 * 1024));
                    var usedMb = (long)((memStatus.ullTotalPhys - memStatus.ullAvailPhys) / (1024 * 1024));
                    return (totalMb, usedMb);
                }
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

    /// <summary>
    /// Bilgisayardaki tüm ağ bağdaştırıcılarını (Adı, Açıklaması, MAC, IP'ler, Alt Ağ Maskeleri, Gateway, DNS, Hız ve Durum) toplar.
    /// </summary>
    public static List<NetworkAdapterInfo> GetNetworkAdapters()
    {
        var list = new List<NetworkAdapterInfo>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProps = nic.GetIPProperties();
                var ips = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork || u.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(u => u.IPv4Mask != null && u.IPv4Mask.ToString() != "0.0.0.0" 
                        ? $"{u.Address} ({u.IPv4Mask})" 
                        : u.Address.ToString())
                    .ToArray();

                var gateways = ipProps.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .ToArray();

                var dns = ipProps.DnsAddresses
                    .Select(d => d.ToString())
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .ToArray();

                var macBytes = nic.GetPhysicalAddress().GetAddressBytes();
                var mac = macBytes.Length > 0 
                    ? string.Join(":", macBytes.Select(b => b.ToString("X2")))
                    : "-";

                var speedMbps = nic.Speed > 0 ? nic.Speed / 1_000_000 : 0;

                list.Add(new NetworkAdapterInfo(
                    Name: nic.Name,
                    Description: nic.Description,
                    Type: nic.NetworkInterfaceType.ToString(),
                    Status: nic.OperationalStatus.ToString(),
                    MacAddress: mac,
                    IpAddresses: ips,
                    Gateways: gateways,
                    DnsServers: dns,
                    SpeedMbps: speedMbps));
            }
        }
        catch { }

        return list;
    }

    private static List<InstalledAppInfo>? _cachedApps;
    private static DateTimeOffset _lastAppsScan = DateTimeOffset.MinValue;
    private static readonly object _appsLock = new();

    /// <summary>
    /// Windows Kayıt Defteri (Registry Uninstall) üzerinden bilgisayarda kurulu olan programların listesini döner.
    /// Performans için 5 dakikalık bellek içi önbellekleme kullanır.
    /// </summary>
    public static List<InstalledAppInfo> GetInstalledApplications(bool forceRefresh = false)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new List<InstalledAppInfo>();
        }

        lock (_appsLock)
        {
            if (!forceRefresh && _cachedApps != null && DateTimeOffset.UtcNow - _lastAppsScan < TimeSpan.FromMinutes(5))
            {
                return _cachedApps;
            }

            var appMap = new Dictionary<string, InstalledAppInfo>(StringComparer.OrdinalIgnoreCase);

            void ScanRegistryKey(RegistryHive hive, RegistryView view, string subKeyPath)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallKey = baseKey.OpenSubKey(subKeyPath);
                    if (uninstallKey == null) return;

                    foreach (var subName in uninstallKey.GetSubKeyNames())
                    {
                        try
                        {
                            using var appKey = uninstallKey.OpenSubKey(subName);
                            if (appKey == null) continue;

                            // System component veya güncelleme ise atla
                            var systemComponent = appKey.GetValue("SystemComponent");
                            if (systemComponent is int scInt && scInt == 1) continue;

                            var parentKeyName = appKey.GetValue("ParentKeyName") as string;
                            if (!string.IsNullOrEmpty(parentKeyName)) continue;

                            var displayName = (appKey.GetValue("DisplayName") as string)?.Trim();
                            if (string.IsNullOrWhiteSpace(displayName)) continue;

                            // Windows Güncellemelerini (KB...) filtrele
                            if (displayName.StartsWith("KB", StringComparison.OrdinalIgnoreCase) && displayName.Length > 2 && char.IsDigit(displayName[2]))
                                continue;
                            if (displayName.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) || 
                                displayName.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var displayVersion = (appKey.GetValue("DisplayVersion") as string)?.Trim();
                            var publisher = (appKey.GetValue("Publisher") as string)?.Trim();
                            var installDate = (appKey.GetValue("InstallDate") as string)?.Trim();
                            
                            // Tarih formatı standardizasyonu (YYYYMMDD -> YYYY-MM-DD)
                            if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8 && int.TryParse(installDate, out _))
                            {
                                installDate = $"{installDate.Substring(0, 4)}-{installDate.Substring(4, 2)}-{installDate.Substring(6, 2)}";
                            }

                            long? estimatedSizeKb = null;
                            var sizeVal = appKey.GetValue("EstimatedSize");
                            if (sizeVal is int sInt) estimatedSizeKb = sInt;
                            else if (sizeVal is long sLong) estimatedSizeKb = sLong;

                            var uninstallString = (appKey.GetValue("UninstallString") as string)?.Trim();
                            var quietUninstallString = (appKey.GetValue("QuietUninstallString") as string)?.Trim();

                            var key = $"{displayName}|{displayVersion}";
                            if (!appMap.ContainsKey(key))
                            {
                                appMap[key] = new InstalledAppInfo(
                                    Name: displayName,
                                    Version: string.IsNullOrWhiteSpace(displayVersion) ? null : displayVersion,
                                    Publisher: string.IsNullOrWhiteSpace(publisher) ? null : publisher,
                                    InstallDate: string.IsNullOrWhiteSpace(installDate) ? null : installDate,
                                    EstimatedSizeKb: estimatedSizeKb,
                                    UninstallString: string.IsNullOrWhiteSpace(uninstallString) ? null : uninstallString,
                                    QuietUninstallString: string.IsNullOrWhiteSpace(quietUninstallString) ? null : quietUninstallString);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // 64-bit ve 32-bit Registry Hive'larını tara
            ScanRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanRegistryKey(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            ScanRegistryKey(RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

            _cachedApps = appMap.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            _lastAppsScan = DateTimeOffset.UtcNow;
            return _cachedApps;
        }
    }

    private static List<WindowsUpdateInfo>? _cachedUpdates;
    private static DateTimeOffset _lastUpdatesScan = DateTimeOffset.MinValue;
    private static readonly object _updatesLock = new();

    /// <summary>
    /// Hedef makinede yüklü olan işletim sistemi güncelleştirmelerini (KB / Hotfixes) Windows Registry (CBS & Uninstall) üzerinden tarar.
    /// 5 dakikalık bellek içi önbellekleme ile CPU ve I/O yükü oluşturmaz.
    /// </summary>
    public static List<WindowsUpdateInfo> GetInstalledWindowsUpdates()
    {
        if (!OperatingSystem.IsWindows())
            return new List<WindowsUpdateInfo>();

        lock (_updatesLock)
        {
            if (_cachedUpdates != null && DateTimeOffset.UtcNow - _lastUpdatesScan < TimeSpan.FromMinutes(5))
            {
                return _cachedUpdates;
            }

            var updateMap = new Dictionary<string, WindowsUpdateInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // 1. Component Based Servicing (CBS) Packages
                using var cbsKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages");
                if (cbsKey != null)
                {
                    foreach (var subKeyName in cbsKey.GetSubKeyNames())
                    {
                        try
                        {
                            var kbIdx = subKeyName.IndexOf("KB", StringComparison.OrdinalIgnoreCase);
                            if (kbIdx < 0) continue;

                            var endIdx = kbIdx + 2;
                            while (endIdx < subKeyName.Length && char.IsDigit(subKeyName[endIdx]))
                            {
                                endIdx++;
                            }
                            if (endIdx <= kbIdx + 2) continue;

                            var kbNumber = subKeyName.Substring(kbIdx, endIdx - kbIdx).ToUpperInvariant();

                            using var pkgKey = cbsKey.OpenSubKey(subKeyName);
                            if (pkgKey == null) continue;

                            var state = pkgKey.GetValue("CurrentState");
                            var stateInt = state is int s ? s : 0;
                            // State 112 (0x70) = Installed, 80 (0x50) = Superseded, 64 (0x40) = Staged
                            if (stateInt != 112 && stateInt != 0x70 && stateInt != 80 && stateInt != 0x50 && stateInt != 64)
                            {
                                if (stateInt == 0) continue;
                            }

                            var releaseType = pkgKey.GetValue("ReleaseType") as string ?? "Güvenlik Güncelleştirmesi";
                            var installTimeHigh = pkgKey.GetValue("InstallTimeHigh") is int h ? h : 0;
                            var installTimeLow = pkgKey.GetValue("InstallTimeLow") is int l ? l : 0;

                            string? installDate = null;
                            if (installTimeHigh != 0 || installTimeLow != 0)
                            {
                                long fileTime = ((long)installTimeHigh << 32) | (uint)installTimeLow;
                                try
                                {
                                    installDate = DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-dd HH:mm");
                                }
                                catch { }
                            }

                            var status = (stateInt == 80 || stateInt == 0x50) ? "Üzerine Yazıldı (Superseded)" : "Yüklü (Başarılı)";

                            if (!updateMap.ContainsKey(kbNumber))
                            {
                                updateMap[kbNumber] = new WindowsUpdateInfo(
                                    HotFixId: kbNumber,
                                    Description: releaseType,
                                    InstalledOn: installDate,
                                    InstalledBy: "NT AUTHORITY\\SYSTEM",
                                    SupportUrl: $"https://support.microsoft.com/help/{kbNumber.Replace("KB", "")}",
                                    Status: status);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                // 2. Uninstall Registry Hives
                void ScanUninstallForUpdates(RegistryHive hive, RegistryView view, string subPath)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                        using var key = baseKey.OpenSubKey(subPath);
                        if (key == null) return;

                        foreach (var subKeyName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using var appKey = key.OpenSubKey(subKeyName);
                                if (appKey == null) continue;

                                var displayName = (appKey.GetValue("DisplayName") as string)?.Trim();
                                if (string.IsNullOrWhiteSpace(displayName)) continue;

                                var kbIdx = displayName.IndexOf("KB", StringComparison.OrdinalIgnoreCase);
                                if (kbIdx < 0)
                                {
                                    if (!displayName.StartsWith("Update for ", StringComparison.OrdinalIgnoreCase) &&
                                        !displayName.StartsWith("Security Update", StringComparison.OrdinalIgnoreCase) &&
                                        !displayName.StartsWith("Hotfix", StringComparison.OrdinalIgnoreCase))
                                        continue;
                                }

                                string kbNumber;
                                if (kbIdx >= 0)
                                {
                                    var endIdx = kbIdx + 2;
                                    while (endIdx < displayName.Length && char.IsDigit(displayName[endIdx])) endIdx++;
                                    kbNumber = displayName.Substring(kbIdx, endIdx - kbIdx).ToUpperInvariant();
                                }
                                else
                                {
                                    kbNumber = subKeyName;
                                }

                                var installDate = (appKey.GetValue("InstallDate") as string)?.Trim();
                                if (!string.IsNullOrEmpty(installDate) && installDate.Length == 8 && int.TryParse(installDate, out _))
                                {
                                    installDate = $"{installDate.Substring(0, 4)}-{installDate.Substring(4, 2)}-{installDate.Substring(6, 2)}";
                                }

                                var helpLink = (appKey.GetValue("HelpLink") as string)?.Trim();

                                if (!updateMap.ContainsKey(kbNumber))
                                {
                                    updateMap[kbNumber] = new WindowsUpdateInfo(
                                        HotFixId: kbNumber,
                                        Description: displayName,
                                        InstalledOn: installDate,
                                        InstalledBy: "Windows Installer",
                                        SupportUrl: helpLink ?? $"https://support.microsoft.com/help/{kbNumber.Replace("KB", "")}",
                                        Status: "Yüklü (Başarılı)");
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }

                ScanUninstallForUpdates(RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                ScanUninstallForUpdates(RegistryHive.LocalMachine, RegistryView.Registry32, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            }
            catch { }

            _cachedUpdates = updateMap.Values
                .OrderByDescending(u => u.InstalledOn ?? string.Empty)
                .ToList();
            _lastUpdatesScan = DateTimeOffset.UtcNow;
            return _cachedUpdates;
        }
    }

    private static HardwareInventoryInfo? _cachedHardware;
    private static DateTimeOffset _lastHardwareScan = DateTimeOffset.MinValue;
    private static readonly object _hardwareLock = new();

    /// <summary>
    /// Cihazın anakart, BIOS, işlemci, RAM modülleri ve fiziksel disklerine ait seri numaraları ve donanım envanter detaylarını toplar.
    /// 30 dakikalık bellek içi önbellekleme kullanır.
    /// </summary>
    public static HardwareInventoryInfo GetHardwareInventory()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new HardwareInventoryInfo(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
        }

        lock (_hardwareLock)
        {
            if (_cachedHardware != null && DateTimeOffset.UtcNow - _lastHardwareScan < TimeSpan.FromMinutes(30))
            {
                return _cachedHardware;
            }

            string? systemSerial = null;
            string? systemVendor = null;
            string? systemModel = null;
            string? systemUuid = null;
            string? biosSerial = null;
            string? biosVersion = null;
            string? biosReleaseDate = null;
            string? mbManufacturer = null;
            string? mbProduct = null;
            string? mbSerial = null;
            string? cpuName = null;
            string? cpuProcId = null;
            int? cpuCores = null;
            int? cpuThreads = null;
            long? cpuMaxClock = null;
            var ramModules = new List<RamModuleInfo>();
            var diskDrives = new List<DiskDriveInfo>();
            var gpus = new List<GpuInfo>();

            try
            {
                // 1. BIOS & System Serial Numbers
                using var biosSearcher = new System.Management.ManagementObjectSearcher("SELECT SerialNumber, Version, Manufacturer, ReleaseDate FROM Win32_BIOS");
                foreach (var obj in biosSearcher.Get())
                {
                    biosSerial = (obj["SerialNumber"] as string)?.Trim();
                    biosVersion = (obj["Version"] as string)?.Trim();
                    biosReleaseDate = (obj["ReleaseDate"] as string)?.Trim();
                    if (!string.IsNullOrEmpty(biosReleaseDate) && biosReleaseDate.Length >= 8)
                    {
                        biosReleaseDate = $"{biosReleaseDate.Substring(0, 4)}-{biosReleaseDate.Substring(4, 2)}-{biosReleaseDate.Substring(6, 2)}";
                    }
                    break;
                }
            }
            catch { }

            try
            {
                // 2. Computer System Product (Chassis / System Serial Number & Model)
                using var csSearcher = new System.Management.ManagementObjectSearcher("SELECT IdentifyingNumber, UUID, Vendor, Name FROM Win32_ComputerSystemProduct");
                foreach (var obj in csSearcher.Get())
                {
                    systemSerial = (obj["IdentifyingNumber"] as string)?.Trim();
                    systemUuid = (obj["UUID"] as string)?.Trim();
                    systemVendor = (obj["Vendor"] as string)?.Trim();
                    systemModel = (obj["Name"] as string)?.Trim();
                    break;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(systemSerial) || systemSerial.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) || systemSerial.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                systemSerial = biosSerial;
            }

            try
            {
                // 3. Motherboard (BaseBoard)
                using var mbSearcher = new System.Management.ManagementObjectSearcher("SELECT SerialNumber, Product, Manufacturer FROM Win32_BaseBoard");
                foreach (var obj in mbSearcher.Get())
                {
                    mbSerial = (obj["SerialNumber"] as string)?.Trim();
                    mbProduct = (obj["Product"] as string)?.Trim();
                    mbManufacturer = (obj["Manufacturer"] as string)?.Trim();
                    break;
                }
            }
            catch { }

            try
            {
                // 4. Processor (CPU)
                using var cpuSearcher = new System.Management.ManagementObjectSearcher("SELECT Name, ProcessorId, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
                foreach (var obj in cpuSearcher.Get())
                {
                    cpuName = (obj["Name"] as string)?.Trim();
                    cpuProcId = (obj["ProcessorId"] as string)?.Trim();
                    if (obj["NumberOfCores"] is uint cores) cpuCores = (int)cores;
                    if (obj["NumberOfLogicalProcessors"] is uint threads) cpuThreads = (int)threads;
                    if (obj["MaxClockSpeed"] is uint clock) cpuMaxClock = clock;
                    break;
                }
            }
            catch { }

            try
            {
                // 5. Physical RAM Modules
                using var ramSearcher = new System.Management.ManagementObjectSearcher("SELECT DeviceLocator, BankLabel, Capacity, Speed, Manufacturer, PartNumber, SerialNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
                foreach (var obj in ramSearcher.Get())
                {
                    var slot = (obj["DeviceLocator"] as string)?.Trim() ?? (obj["BankLabel"] as string)?.Trim() ?? $"Slot #{ramModules.Count + 1}";
                    var mfg = (obj["Manufacturer"] as string)?.Trim();
                    var part = (obj["PartNumber"] as string)?.Trim();
                    var serial = (obj["SerialNumber"] as string)?.Trim();
                    long capMb = 0;
                    if (obj["Capacity"] is ulong cap) capMb = (long)(cap / (1024 * 1024));
                    else if (obj["Capacity"] is string capStr && ulong.TryParse(capStr, out var cVal)) capMb = (long)(cVal / (1024 * 1024));
                    
                    int? speed = null;
                    if (obj["Speed"] is uint spd) speed = (int)spd;

                    string? memType = null;
                    if (obj["SMBIOSMemoryType"] is uint smType)
                    {
                        memType = smType switch
                        {
                            20 => "DDR",
                            21 => "DDR2",
                            24 => "DDR3",
                            26 => "DDR4",
                            34 => "DDR5",
                            30 => "LPDDR4",
                            35 => "LPDDR5",
                            _ => "RAM"
                        };
                    }

                    if (capMb > 0)
                    {
                        ramModules.Add(new RamModuleInfo(
                            BankLabel: slot,
                            Manufacturer: string.IsNullOrWhiteSpace(mfg) || mfg.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : mfg,
                            PartNumber: string.IsNullOrWhiteSpace(part) || part.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : part,
                            SerialNumber: string.IsNullOrWhiteSpace(serial) || serial.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : serial,
                            CapacityMb: capMb,
                            SpeedMhz: speed,
                            MemoryType: memType));
                    }
                }
            }
            catch { }

            try
            {
                // 6. Physical Disk Drives
                using var diskSearcher = new System.Management.ManagementObjectSearcher("SELECT Model, SerialNumber, InterfaceType, MediaType, Size, Partitions FROM Win32_DiskDrive");
                foreach (var obj in diskSearcher.Get())
                {
                    var model = (obj["Model"] as string)?.Trim() ?? "Fiziksel Disk";
                    var serial = (obj["SerialNumber"] as string)?.Trim();
                    var iface = (obj["InterfaceType"] as string)?.Trim();
                    var media = (obj["MediaType"] as string)?.Trim();
                    long sizeGb = 0;
                    if (obj["Size"] is ulong sz) sizeGb = (long)(sz / (1024 * 1024 * 1024));
                    else if (obj["Size"] is string szStr && ulong.TryParse(szStr, out var szVal)) sizeGb = (long)(szVal / (1024 * 1024 * 1024));

                    int? parts = null;
                    if (obj["Partitions"] is uint p) parts = (int)p;

                    diskDrives.Add(new DiskDriveInfo(
                        Model: model,
                        SerialNumber: string.IsNullOrWhiteSpace(serial) ? null : serial,
                        InterfaceType: iface,
                        MediaType: media,
                        SizeGb: sizeGb,
                        PartitionsCount: parts));
                }
            }
            catch { }

            try
            {
                // 7. Video Controllers (GPU)
                using var gpuSearcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM, VideoProcessor FROM Win32_VideoController");
                foreach (var obj in gpuSearcher.Get())
                {
                    var name = (obj["Name"] as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var drv = (obj["DriverVersion"] as string)?.Trim();
                    var proc = (obj["VideoProcessor"] as string)?.Trim();
                    long? vramMb = null;
                    if (obj["AdapterRAM"] is uint vram) vramMb = (long)(vram / (1024 * 1024));

                    gpus.Add(new GpuInfo(
                        Name: name,
                        DriverVersion: drv,
                        VramMb: vramMb,
                        VideoProcessor: proc));
                }
            }
            catch { }

            // Registry Fallbacks if WMI is unavailable
            try
            {
                using var biosKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
                if (biosKey != null)
                {
                    if (string.IsNullOrWhiteSpace(systemSerial)) systemSerial = (biosKey.GetValue("SystemSerialNumber") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(systemVendor)) systemVendor = (biosKey.GetValue("SystemManufacturer") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(systemModel)) systemModel = (biosKey.GetValue("SystemProductName") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(biosVersion)) biosVersion = (biosKey.GetValue("BIOSVersion") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(biosReleaseDate)) biosReleaseDate = (biosKey.GetValue("BIOSReleaseDate") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(mbManufacturer)) mbManufacturer = (biosKey.GetValue("BaseBoardManufacturer") as string)?.Trim();
                    if (string.IsNullOrWhiteSpace(mbProduct)) mbProduct = (biosKey.GetValue("BaseBoardProduct") as string)?.Trim();
                }
            }
            catch { }

            _cachedHardware = new HardwareInventoryInfo(
                SystemSerialNumber: CleanSerial(systemSerial),
                SystemManufacturer: systemVendor,
                SystemModel: systemModel,
                SystemUuid: systemUuid,
                BiosSerialNumber: CleanSerial(biosSerial),
                BiosVersion: biosVersion,
                BiosReleaseDate: biosReleaseDate,
                MotherboardManufacturer: mbManufacturer,
                MotherboardProduct: mbProduct,
                MotherboardSerialNumber: CleanSerial(mbSerial),
                CpuName: cpuName,
                CpuProcessorId: CleanSerial(cpuProcId),
                CpuCores: cpuCores,
                CpuLogicalProcessors: cpuThreads,
                CpuMaxClockSpeedMhz: cpuMaxClock,
                RamModules: ramModules.Count > 0 ? ramModules : null,
                DiskDrives: diskDrives.Count > 0 ? diskDrives : null,
                GraphicsCards: gpus.Count > 0 ? gpus : null);

            _lastHardwareScan = DateTimeOffset.UtcNow;
            return _cachedHardware;
        }
    }

    private static string? CleanSerial(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        if (t.Equals("To Be Filled By O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("System Serial Number", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return t;
    }
}
