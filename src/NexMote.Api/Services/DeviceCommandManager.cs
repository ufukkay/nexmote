using System.Collections.Concurrent;

namespace NexMote.Api.Services;

/// <summary>
/// Web konsolundan gönderilen komutun hedef ajandaki çalışma sonucu DTO nesnesi.
/// </summary>
public sealed record DeviceCommandExecutionResult(
    Guid RequestId,
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    bool TimedOut,
    bool ElevationDenied);

/// <summary>
/// Web konsolundan doğrudan tetiklenen uzak terminal komutlarının (CMD / PowerShell)
/// istek ve yanıtlarını (TaskCompletionSource) yöneten singleton servis.
/// </summary>
public sealed class DeviceCommandManager
{
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<DeviceCommandExecutionResult>> _pendingCommands = new();

    /// <summary>
    /// Yeni bir komut isteği için asenkron bekleme tanımlar.
    /// </summary>
    public TaskCompletionSource<DeviceCommandExecutionResult> RegisterCommand(Guid requestId)
    {
        var tcs = new TaskCompletionSource<DeviceCommandExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[requestId] = tcs;
        return tcs;
    }

    /// <summary>
    /// Ajan tarafından SignalR üzerinden dönülen komut sonucunu tamamlar ve bekleyen HTTP isteğini çözer.
    /// </summary>
    public bool CompleteCommand(DeviceCommandExecutionResult result)
    {
        if (_pendingCommands.TryRemove(result.RequestId, out var tcs))
        {
            return tcs.TrySetResult(result);
        }
        return false;
    }

    /// <summary>
    /// Zaman aşımı veya iptal durumunda bekleyen isteği sonlandırır.
    /// </summary>
    public void CancelCommand(Guid requestId)
    {
        if (_pendingCommands.TryRemove(requestId, out var tcs))
        {
            tcs.TrySetCanceled();
        }
    }
}
