namespace NexMote.Shared.Contracts;

/// <summary>
/// Hedef bilgisayarda yürütülen uzak komutun çıktısını ve durumunu teknisyene döndüren sonuç kontratı.
/// </summary>
/// <param name="SessionId">Oturum kimliği.</param>
/// <param name="RequestId">İstek talep kimliği.</param>
/// <param name="ExitCode">İşlem çıkış kodu (0 = Başarılı).</param>
/// <param name="StdOut">Standart konsol çıktısı.</param>
/// <param name="StdErr">Konsol hata çıktısı.</param>
/// <param name="DurationMs">Yürütülme süresi (milisaniye).</param>
/// <param name="TimedOut">Komutun zaman aşımına uğrayıp uğramadığı.</param>
/// <param name="ElevationDenied">UAC yönetici ayrıcalığı reddedildiyse true döner.</param>
public sealed record RemoteCommandResult(
    Guid SessionId,
    string RequestId,
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    bool TimedOut,
    bool ElevationDenied = false);
