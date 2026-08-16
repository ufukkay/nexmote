namespace NexMote.Shared.Contracts;

/// <summary>
/// Teknisyen uygulamasından hedef makineye veya hedef makineden teknisyene dosya aktarımı için kullanılan parça (chunk) kontratı.
/// </summary>
/// <param name="SessionId">Aktif bağlantı oturum kimliği.</param>
/// <param name="TransferId">Aktarıma ait benzersiz transfer kimliği.</param>
/// <param name="FileName">Hedef dosya adı.</param>
/// <param name="TotalSize">Dosyanın toplam bayt boyutu.</param>
/// <param name="ChunkIndex">Mevcut parçanın dizin numarası (0-indexed).</param>
/// <param name="TotalChunks">Toplam parça sayısı.</param>
/// <param name="Base64Data">Parçanın Base64 kodlanmış veri gövdesi.</param>
/// <param name="IsLast">Bu parçanın dosyanın son parçası olup olmadığı.</param>
public sealed record FileTransferChunk(
    Guid SessionId,
    Guid TransferId,
    string FileName,
    long TotalSize,
    int ChunkIndex,
    int TotalChunks,
    string Base64Data,
    bool IsLast);
