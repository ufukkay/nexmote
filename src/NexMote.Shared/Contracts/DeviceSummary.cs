namespace NexMote.Shared.Contracts;

/// <summary>
/// Web konsolu ve teknisyen istemcisinde listelenen cihazın özet bilgileri ve donanım metrikleri.
/// </summary>
/// <param name="Id">Cihazın benzersiz Guid kimliği.</param>
/// <param name="DeviceName">Bilgisayar adı.</param>
/// <param name="DomainName">Çalışma grubu veya Domain adı.</param>
/// <param name="OperatingSystem">İşletim sistemi sürümü.</param>
/// <param name="AgentVersion">Yüklü Agent sürümü.</param>
/// <param name="ActiveUser">Oturum açmış kullanıcı adı.</param>
/// <param name="IpAddress">Yerel/dış IP adresi.</param>
/// <param name="LocationCode">Lokasyon kodu.</param>
/// <param name="IsOnline">Cihazın çevrimiçi olup olmadığı (son 2 dakika içinde sinyal kontrolü).</param>
/// <param name="LastSeenAt">Son sinyal alınma zamanı.</param>
/// <param name="CpuUsagePercent">CPU kullanım yüzdesi.</param>
/// <param name="MemoryTotalMb">Toplam fiziksel bellek (MB).</param>
/// <param name="MemoryUsedMb">Kullanılan bellek (MB).</param>
/// <param name="DiskFreeMb">Boş disk alanı (MB).</param>
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
    long DiskFreeMb = 0);
