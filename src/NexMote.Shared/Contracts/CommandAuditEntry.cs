namespace NexMote.Shared.Contracts;

/// <summary>
/// İstemci üzerinde çalıştırılan uzak komutların (CMD/PowerShell) denetim (audit) kaydı kontratı.
/// </summary>
/// <param name="DeviceId">Komutun çalıştırıldığı cihazın kimliği.</param>
/// <param name="AgentToken">Cihaza ait güvenlik doğrulama token'ı.</param>
/// <param name="SessionId">Komutun bağlı olduğu teknisyen oturum kimliği.</param>
/// <param name="Shell">Kullanılan kabuk türü ("cmd" veya "powershell").</param>
/// <param name="Command">Yürütülen komut metni.</param>
/// <param name="ExitCode">İşlemin çıkış kodu (0 = Başarılı).</param>
/// <param name="StdOutPreview">Standart çıktı özeti (ilk 2000 karakter).</param>
/// <param name="StdErrPreview">Hata çıktısı özeti (varsa, ilk 2000 karakter).</param>
/// <param name="DurationMs">Komutun çalışma süresi (milisaniye).</param>
/// <param name="ExecutedAt">Komutun yürütüldüğü zaman damgası.</param>
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
