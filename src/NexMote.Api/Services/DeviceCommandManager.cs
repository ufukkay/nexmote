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
    private readonly ConcurrentDictionary<Guid, PendingDeviceCommand> _pendingCommands = new();

    /// <summary>
    /// Yeni bir komut isteği için asenkron bekleme tanımlar.
    /// </summary>
    public TaskCompletionSource<DeviceCommandExecutionResult> RegisterCommand(Guid requestId, Guid deviceId)
    {
        var tcs = new TaskCompletionSource<DeviceCommandExecutionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[requestId] = new PendingDeviceCommand(deviceId, tcs);
        return tcs;
    }

    /// <summary>
    /// Ajan tarafından SignalR üzerinden dönülen komut sonucunu tamamlar ve bekleyen HTTP isteğini çözer.
    /// </summary>
    public bool CompleteCommand(Guid deviceId, DeviceCommandExecutionResult result)
    {
        if (_pendingCommands.TryGetValue(result.RequestId, out var pending) && pending.DeviceId == deviceId)
        {
            return _pendingCommands.TryRemove(result.RequestId, out _) && pending.Completion.TrySetResult(result);
        }
        return false;
    }

    /// <summary>
    /// Zaman aşımı veya iptal durumunda bekleyen isteği sonlandırır.
    /// </summary>
    public void CancelCommand(Guid requestId)
    {
        if (_pendingCommands.TryRemove(requestId, out var pending))
        {
            pending.Completion.TrySetCanceled();
        }
    }
}

internal sealed record PendingDeviceCommand(Guid DeviceId, TaskCompletionSource<DeviceCommandExecutionResult> Completion);
