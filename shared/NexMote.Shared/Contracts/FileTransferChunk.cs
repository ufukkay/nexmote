namespace NexMote.Shared.Contracts;

public sealed record FileTransferChunk(
    Guid SessionId,
    Guid TransferId,
    string FileName,
    long TotalSize,
    int ChunkIndex,
    int TotalChunks,
    string Base64Data,
    bool IsLast);
