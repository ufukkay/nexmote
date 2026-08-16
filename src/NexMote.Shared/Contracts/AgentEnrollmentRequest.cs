namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef Windows makinesine kurulan Agent'ın sunucuya ilk kaydını (Enrollment) yapmak için gönderdiği istek.
/// </summary>
/// <param name="EnrollmentKey">Sunucuda tanımlı olan ortak kayıt anahtarı.</param>
/// <param name="DeviceName">Cihazın bilgisayar adı (Hostname).</param>
/// <param name="DomainName">Cihazın çalışma grubu veya Active Directory Domain adı.</param>
/// <param name="OperatingSystem">İşletim sistemi sürüm bilgisi (Örn: Windows 11 Pro).</param>
/// <param name="AgentVersion">Yüklü olan NexMote Agent yazılım sürümü (Örn: 0.5.0).</param>
/// <param name="SerialNumber">Cihaz seri numarası (opsiyonel).</param>
/// <param name="LocationCode">Cihaz lokasyon veya departman kodu (Örn: OFFICE, LAB).</param>
public sealed record AgentEnrollmentRequest(
    string EnrollmentKey,
    string DeviceName,
    string DomainName,
    string OperatingSystem,
    string AgentVersion,
    string? SerialNumber,
    string LocationCode);
