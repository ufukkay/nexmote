using System.Diagnostics;
using System.Text;

namespace NexMote.Shared.Commands;

/// <summary>
/// Komut çalıştırma sonucu.
/// </summary>
public sealed record CommandRunResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    long DurationMs,
    bool TimedOut,
    bool ElevationDenied = false);

/// <summary>
/// CMD veya PowerShell komutlarını standart I/O yönlendirmesi ile sessizce yürüten motor.
/// Windows Servisi (LocalSystem) içerisinde çağrıldığında doğrudan tam NT AUTHORITY\SYSTEM (Yönetici) ayrıcalığıyla çalışır.
/// </summary>
public static class CommandRunner
{
    public static async Task<CommandRunResult> RunAsync(string shell, string command, int timeoutMs = 60000)
    {
        var stopwatch = Stopwatch.StartNew();
        var isPowerShell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var fileName = isPowerShell ? "powershell.exe" : "cmd.exe";
        string arguments;
        if (isPowerShell)
        {
            var utf8Preamble = "$OutputEncoding = [Console]::OutputEncoding = [Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false); $ProgressPreference = 'SilentlyContinue'; ";
            var bytes = Encoding.Unicode.GetBytes(utf8Preamble + command);
            var base64 = Convert.ToBase64String(bytes);
            arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {base64}";
        }
        else
        {
            arguments = $"/c \"chcp 65001 >nul & {command}\"";
        }

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stdErr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new CommandRunResult(-1, string.Empty, ex.Message, stopwatch.ElapsedMilliseconds, false);
        }

        var timedOut = false;
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try { process.Kill(entireProcessTree: true); } catch { }
        }

        stopwatch.Stop();
        return new CommandRunResult(
            timedOut ? -1 : process.ExitCode,
            stdOut.ToString(),
            stdErr.ToString(),
            stopwatch.ElapsedMilliseconds,
            timedOut);
    }
}
