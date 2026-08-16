using System.Collections.Concurrent;

namespace NexMote.Agent.Windows.Logging;

/// <summary>
/// Windows Servisi günlük kayıtlarını diskteki log dosyasına (%ProgramData%\NexMote\Logs\agent-service.log)
/// iş parçacığı güvenli (thread-safe) biçimde yazan sağlayıcı sınıfı.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly StreamWriter _writer;
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logPath)
    {
        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _writer, _syncRoot));
    }

    public void Dispose()
    {
        _writer.Dispose();
    }
}

/// <summary>
/// Belirli bir kategoriye ait log satırlarını biçimlendirip dosyaya yazan loglayıcı.
/// </summary>
public sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly StreamWriter _writer;
    private readonly object _syncRoot;

    public FileLogger(string categoryName, StreamWriter writer, object syncRoot)
    {
        _categoryName = categoryName;
        _writer = writer;
        _syncRoot = syncRoot;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{logLevel}] {_categoryName}: {message}";

        lock (_syncRoot)
        {
            _writer.WriteLine(line);
            if (exception is not null)
            {
                _writer.WriteLine(exception);
            }
        }
    }
}
