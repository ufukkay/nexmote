using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NexMote.Agent.Tray;

/// <summary>
/// İzole <c>--capture-helper</c> alt sürecini tembel (lazy) başlatan, adlandırılmış boru üzerinden ekran
/// başına bağlantı tutan ve alt süreç çökerse otomatik (geri çekilmeli) yeniden başlatan istemci.
/// Tray sürecinde tek örnek olarak, süreç ömrü boyunca yaşar. Bkz. <see cref="CaptureHelperServer"/>.
/// </summary>
internal sealed class CaptureHelperClient : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly object _processLock = new();
    private readonly ConcurrentDictionary<int, DisplayConnection> _connections = new();

    private Process? _helperProcess;
    private string? _pipeName;
    private long _nextStartAttemptTicks;
    private long _lastStartTicks;
    private double _restartBackoffMs = 500;
    private bool _disposed;

    public async Task<string?> CaptureJpegBase64Async(int displayIndex, int quality, bool forceSend, bool resetHash, CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            return null;
        }

        EnsureHelperProcessRunning();
        var pipeName = _pipeName;
        if (pipeName is null)
        {
            return null;
        }

        var conn = _connections.GetOrAdd(displayIndex, _ => new DisplayConnection());
        await conn.Lock.WaitAsync(cancellationToken);
        try
        {
            if (!await conn.EnsureConnectedAsync(pipeName, cancellationToken))
            {
                return null;
            }

            var ioTask = SendAndReceiveAsync(conn, displayIndex, quality, forceSend, resetHash);
            var delayTask = Task.Delay(750, cancellationToken);
            var completed = await Task.WhenAny(ioTask, delayTask);
            if (completed != ioTask)
            {
                // Zaman aşımı: alt süreç muhtemelen çökmüş/donmuş — bağlantıyı at, bir sonraki
                // çağrıda taze bir bağlantı denenecek.
                conn.Disconnect();
                return null;
            }

            return await ioTask;
        }
        catch
        {
            conn.Disconnect();
            return null;
        }
        finally
        {
            conn.Lock.Release();
        }
    }

    private static async Task<string?> SendAndReceiveAsync(DisplayConnection conn, int displayIndex, int quality, bool forceSend, bool resetHash)
    {
        var request = JsonSerializer.Serialize(new CaptureRequest(displayIndex, quality, forceSend, resetHash), JsonOptions);
        await conn.Writer!.WriteLineAsync(request);

        var responseLine = await conn.Reader!.ReadLineAsync();
        if (string.IsNullOrEmpty(responseLine))
        {
            conn.Disconnect();
            return null;
        }

        var response = JsonSerializer.Deserialize<CaptureResponse>(responseLine, JsonOptions);
        return response?.JpegBase64;
    }

    private void EnsureHelperProcessRunning()
    {
        lock (_processLock)
        {
            if (_helperProcess is { HasExited: false })
            {
                // 30 saniyedir sorunsuz çalışıyorsa geri çekilme süresini sıfırla; bir sonraki
                // gerçek çökmede yeniden küçük bir gecikmeyle başlanır.
                if (_lastStartTicks != 0 && (Stopwatch.GetTimestamp() - _lastStartTicks) > Stopwatch.Frequency * 30)
                {
                    _restartBackoffMs = 500;
                }
                return;
            }

            if (Stopwatch.GetTimestamp() < _nextStartAttemptTicks)
            {
                return;
            }

            try
            {
                _helperProcess?.Dispose();

                // Eski (artık geçersiz) pipe adına bağlı kalan bağlantıları at; bir sonraki çağrıda
                // yeni pipe adıyla taze bağlanılacak.
                foreach (var conn in _connections.Values)
                {
                    conn.Disconnect();
                }
                _connections.Clear();

                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    return;
                }

                _pipeName = $"NexMoteCaptureHelper_{Guid.NewGuid():N}";
                var psi = new ProcessStartInfo(exePath, $"--capture-helper --pipe={_pipeName} --parent-pid={Environment.ProcessId}")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _helperProcess = Process.Start(psi);
                _lastStartTicks = Stopwatch.GetTimestamp();

                _nextStartAttemptTicks = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * _restartBackoffMs / 1000);
                _restartBackoffMs = Math.Min(_restartBackoffMs * 2, 8000);
            }
            catch
            {
                _helperProcess = null;
                _pipeName = null;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;

        foreach (var conn in _connections.Values)
        {
            conn.Disconnect();
        }
        _connections.Clear();

        lock (_processLock)
        {
            try
            {
                if (_helperProcess is { HasExited: false })
                {
                    _helperProcess.Kill(entireProcessTree: true);
                }
                _helperProcess?.Dispose();
            }
            catch
            {
            }
            _helperProcess = null;
        }

        await Task.CompletedTask;
    }

    private sealed class DisplayConnection
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);
        public NamedPipeClientStream? Pipe { get; private set; }
        public StreamWriter? Writer { get; private set; }
        public StreamReader? Reader { get; private set; }
        private long _nextConnectAttemptTicks;

        public async Task<bool> EnsureConnectedAsync(string pipeName, CancellationToken cancellationToken)
        {
            if (Pipe is { IsConnected: true } && Writer is not null && Reader is not null)
            {
                return true;
            }

            if (Stopwatch.GetTimestamp() < _nextConnectAttemptTicks)
            {
                return false;
            }

            Disconnect();

            try
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500, cancellationToken);
                Pipe = pipe;
                Writer = new StreamWriter(pipe, Encoding.UTF8, 1024 * 1024, leaveOpen: true) { AutoFlush = true };
                Reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
                return true;
            }
            catch
            {
                _nextConnectAttemptTicks = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 2);
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            try { Writer?.Dispose(); } catch { }
            try { Reader?.Dispose(); } catch { }
            try { Pipe?.Dispose(); } catch { }
            Writer = null;
            Reader = null;
            Pipe = null;
        }
    }
}
