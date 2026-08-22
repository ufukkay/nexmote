namespace NexMote.Shared.Contracts;

/// <summary>
/// Teknisyen tarafından belirli bir cihaza canlı bağlantı oturumu başlatma isteği.
/// </summary>
public sealed record CreateRemoteSessionRequest(Guid DeviceId);

/// <summary>
/// Başlatılan uzaktan kontrol oturumunun deep-link ve yetkilendirme detayları.
/// </summary>
public sealed record CreateRemoteSessionResponse(
    Guid SessionId,
    Guid DeviceId,
    string LaunchUri,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Hedef bilgisayara uzaktan güç eylemi (yeniden başlatma, kapatma veya kilitleme) tetikleme isteği.
/// </summary>
public sealed record PowerActionRequest(Guid SessionId, string Action);

/// <summary>
/// Hedef bilgisayarda uzaktan CMD veya PowerShell komutu çalıştırma isteği.
/// </summary>
public sealed record RemoteCommandRequest(
    Guid SessionId,
    string RequestId,
    string Shell,
    string Command,
    bool RunAsAdmin = true);

/// <summary>
/// Hedef bilgisayarda yürütülen uzak komutun çıktısını ve durumunu teknisyene döndüren sonuç kontratı.
/// </summary>
public sealed record RemoteCommandResult(
    Guid SessionId,
    string RequestId,
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    bool TimedOut,
    bool ElevationDenied = false);

/// <summary>
/// Teknisyen uygulamasından hedef makineye veya hedef makineden teknisyene dosya aktarımı için kullanılan parça (chunk) kontratı.
/// </summary>
public sealed record FileTransferChunk(
    Guid SessionId,
    Guid TransferId,
    string FileName,
    long TotalSize,
    int ChunkIndex,
    int TotalChunks,
    string Base64Data,
    bool IsLast);
