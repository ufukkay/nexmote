namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef Windows makinesine kurulan Agent'ın sunucuya ilk kaydını (Enrollment) yapmak için gönderdiği istek.
/// </summary>
public sealed record AgentEnrollmentRequest(
    string EnrollmentKey,
    string DeviceName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    string? SerialNumber,
    string LocationCode);

/// <summary>
/// Sunucu tarafından cihaz kaydı onaylandığında Agent'a iletilen yanıt.
/// </summary>
public sealed record AgentEnrollmentResponse(
    Guid DeviceId,
    string AgentToken,
    Uri SignalingHubPath,
    TimeSpan HeartbeatInterval);

/// <summary>
/// Bilgisayardaki bir ağ bağdaştırıcısına (Ethernet, Wi-Fi vb.) ait detaylı IP, MAC, DNS ve Gateway bilgileri.
/// </summary>
public sealed record NetworkAdapterInfo(
    string Name,
    string Description,
    string Type,
    string Status,
    string MacAddress,
    string[] IpAddresses,
    string[] Gateways,
    string[] DnsServers,
    long SpeedMbps);

/// <summary>
/// Bilgisayarda yüklü bir programa (yazılıma) ait ad, sürüm, yayımcı, yükleme tarihi ve boyut detayları.
/// </summary>
public sealed record InstalledAppInfo(
    string Name,
    string? Version,
    string? Publisher,
    string? InstallDate,
    long? EstimatedSizeKb,
    string? UninstallString = null,
    string? QuietUninstallString = null);

/// <summary>
/// Bilgisayarda yüklü olan işletim sistemi güncelleştirmesi / KB / Hotfix bilgisi.
/// </summary>
public sealed record WindowsUpdateInfo(
    string HotFixId,
    string? Description,
    string? InstalledOn,
    string? InstalledBy = null,
    string? SupportUrl = null,
    string? Status = "Installed");

/// <summary>
/// Bilgisayarda yüklü olan bir RAM (fiziksel bellek) modülüne ait slot, üretici, parça no, seri no, kapasite ve hız detayları.
/// </summary>
public sealed record RamModuleInfo(
    string BankLabel,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    long CapacityMb,
    int? SpeedMhz,
    string? MemoryType);

/// <summary>
/// Bilgisayara takılı olan bir fiziksel depolama sürücüsüne (NVMe, SSD, HDD) ait model, seri numarası, boyut ve arayüz detayları.
/// </summary>
public sealed record DiskDriveInfo(
    string Model,
    string? SerialNumber,
    string? InterfaceType,
    string? MediaType,
    long SizeGb,
    int? PartitionsCount);

/// <summary>
/// Bilgisayardaki ekran kartına (GPU / Video Denetleyici) ait model, sürücü sürümü ve VRAM detayları.
/// </summary>
public sealed record GpuInfo(
    string Name,
    string? DriverVersion,
    long? VramMb,
    string? VideoProcessor);

/// <summary>
/// Cihazın anakart, BIOS, işlemci, RAM modülleri ve fiziksel disklerine ait seri numaraları ve donanım envanter detayları.
/// </summary>
public sealed record HardwareInventoryInfo(
    string? SystemSerialNumber,
    string? SystemManufacturer,
    string? SystemModel,
    string? SystemUuid,
    string? BiosSerialNumber,
    string? BiosVersion,
    string? BiosReleaseDate,
    string? MotherboardManufacturer,
    string? MotherboardProduct,
    string? MotherboardSerialNumber,
    string? CpuName,
    string? CpuProcessorId,
    int? CpuCores,
    int? CpuLogicalProcessors,
    long? CpuMaxClockSpeedMhz,
    List<RamModuleInfo>? RamModules = null,
    List<DiskDriveInfo>? DiskDrives = null,
    List<GpuInfo>? GraphicsCards = null);

/// <summary>
/// Hedef bilgisayardaki Windows Servisi tarafından periyodik olarak sunucuya iletilen canlılık ve donanım telemetrisi paketi.
/// </summary>
public sealed record DeviceHeartbeatRequest(
    string AgentToken,
    string ActiveUser,
    string IpAddress,
    int CpuUsagePercent,
    long MemoryTotalMb,
    long MemoryUsedMb,
    long DiskFreeMb,
    long UptimeSeconds,
    string AgentVersion,
    List<NetworkAdapterInfo>? NetworkAdapters = null,
    List<InstalledAppInfo>? InstalledApps = null,
    List<WindowsUpdateInfo>? WindowsUpdates = null,
    string? SerialNumber = null,
    HardwareInventoryInfo? HardwareDetails = null);

/// <summary>
/// Web konsolu ve teknisyen istemcisinde listelenen cihazın özet bilgileri ve donanım metrikleri.
/// </summary>
public sealed record DeviceSummary(
    Guid Id,
    string DeviceName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    string? ActiveUser,
    string? IpAddress,
    string? LocationCode,
    bool IsOnline,
    DateTimeOffset LastSeenAt,
    double CpuUsagePercent = 0,
    long MemoryTotalMb = 0,
    long MemoryUsedMb = 0,
    long DiskFreeMb = 0,
    List<NetworkAdapterInfo>? NetworkAdapters = null,
    List<InstalledAppInfo>? InstalledApps = null,
    List<WindowsUpdateInfo>? WindowsUpdates = null,
    string? SerialNumber = null,
    HardwareInventoryInfo? HardwareDetails = null,
    Guid? SecurityProfileId = null);

/// <summary>
/// İstemci üzerinde çalıştırılan uzak komutların (CMD/PowerShell) denetim (audit) kaydı kontratı.
/// </summary>
public sealed record CommandAuditEntry(
    Guid DeviceId,
    string AgentToken,
    Guid SessionId,
    string Shell,
    string Command,
    int ExitCode,
    string StdOutPreview,
    string StdErrPreview,
    long DurationMs,
    DateTimeOffset ExecutedAt);
