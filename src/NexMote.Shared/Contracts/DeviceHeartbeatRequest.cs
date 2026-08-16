namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef bilgisayardaki Windows Servisi tarafından periyodik olarak (varsayılan 20 saniyede bir) sunucuya iletilen canlılık ve donanım telemetrisi paketi.
/// </summary>
/// <param name="AgentToken">Cihazın kimlik doğrulama token'ı.</param>
/// <param name="ActiveUser">Oturum açmış kullanıcı veya makine hesabı.</param>
/// <param name="IpAddress">Cihazın birincil fiziksel yerel IPv4 adresi.</param>
/// <param name="CpuUsagePercent">Win32 GetSystemTimes ile ölçülen 10 dakikalık kayan pencere ortalama CPU kullanım yüzdesi (0-100).</param>
/// <param name="MemoryTotalMb">Cihazın toplam fiziksel RAM miktarı (Megabayt cinsinden).</param>
/// <param name="MemoryUsedMb">Cihazın anlık kullanılan RAM miktarı (Megabayt cinsinden).</param>
/// <param name="DiskFreeMb">Sistem sürücüsündeki (C:\) boş disk alanı (Megabayt cinsinden).</param>
/// <param name="UptimeSeconds">Sistemin açık kalma süresi (Saniye cinsinden).</param>
/// <param name="AgentVersion">Cihazda anlık çalışan Agent yazılım sürümü (Örn: 0.5.0).</param>
public sealed record DeviceHeartbeatRequest(
    string AgentToken,
    string ActiveUser,
    string IpAddress,
    int CpuUsagePercent,
    long MemoryTotalMb,
    long MemoryUsedMb,
    long DiskFreeMb,
    long UptimeSeconds,
    string AgentVersion);
