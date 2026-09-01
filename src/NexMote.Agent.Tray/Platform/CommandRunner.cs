using System.Diagnostics;
using System.Text;

namespace NexMote.Agent.Tray;

internal sealed record CommandRunResult(int ExitCode, string StdOut, string StdErr, long DurationMs, bool TimedOut, bool ElevationDenied = false);

/// <summary>
/// Uzaktan gelen CMD veya PowerShell komutlarını standart veya UAC yükseltmeli ("runas") modda çalıştıran ve denetim loglarını sunucuya gönderen motor.
/// </summary>
internal static class CommandRunner
{
    public static Task<CommandRunResult> RunAsync(string shell, string command, int timeoutMs, bool runAsAdmin = false)
    {
        // Hedef cihazda UAC veya Domain Admin şifre istemi çıkmaması için komutlar her zaman sessiz arka plan modunda (I/O redirection ile) yürütülür.
        return RunStandardAsync(shell, command, timeoutMs);
    }

    /// <summary>
    /// Launches the command with the Windows "runas" verb, which makes the real UAC
    /// consent/credential prompt appear on the target machine's desktop. UseShellExecute+runas
    /// cannot share stdio pipes with the parent, so the elevated process is wrapped to redirect
    /// its own output into a temp file that we read back after it exits.
    /// </summary>
    private static async Task<CommandRunResult> RunElevatedAsync(string shell, string command, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var isPowerShell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var fileName = isPowerShell ? "powershell.exe" : "cmd.exe";
        var outFile = Path.Combine(Path.GetTempPath(), $"nexmote_elevated_{Guid.NewGuid():N}.txt");
        string? tempScript = null;
        string arguments;

        if (isPowerShell)
        {
            var psScript = $"{command} *>&1 | Out-File -FilePath '{outFile}' -Encoding utf8";
            var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
            arguments = $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encodedScript}";
        }
        else
        {
            tempScript = Path.Combine(Path.GetTempPath(), $"nexmote_cmd_{Guid.NewGuid():N}.cmd");
            var batchContent = $"@echo off\r\nchcp 65001 >nul\r\n{command} > \"{outFile}\" 2>&1\r\n";
            await File.WriteAllTextAsync(tempScript, batchContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            arguments = $"/c \"{tempScript}\"";
        }

        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        };

        var timedOut = false;
        var elevationDenied = false;
        var exitCode = -1;

        try
        {
            using var process = Process.Start(psi);
            if (process is not null)
            {
                using var cts = new CancellationTokenSource(timeoutMs);
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    exitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    timedOut = true;
                    try { process.Kill(entireProcessTree: true); } catch { }
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            elevationDenied = true;
        }
        finally
        {
            if (tempScript is not null && File.Exists(tempScript))
            {
                try { File.Delete(tempScript); } catch { }
            }
        }

        stopwatch.Stop();

        var output = string.Empty;
        try
        {
            if (File.Exists(outFile))
            {
                output = await File.ReadAllTextAsync(outFile);
                File.Delete(outFile);
            }
        }
        catch
        {
        }

        return new CommandRunResult(
            elevationDenied || timedOut ? -1 : exitCode,
            output,
            elevationDenied ? "Kullanıcı yönetici izni istemini reddetti veya kimlik doğrulaması başarısız oldu." : string.Empty,
            stopwatch.ElapsedMilliseconds,
            timedOut,
            elevationDenied);
    }

    private static async Task<CommandRunResult> RunStandardAsync(string shell, string command, int timeoutMs)
    {
        var stopwatch = Stopwatch.StartNew();
        var isPowerShell = string.Equals(shell, "powershell", StringComparison.OrdinalIgnoreCase);
        var fileName = isPowerShell ? "powershell.exe" : "cmd.exe";
        var arguments = isPowerShell
            ? $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\""
            : $"/c {command}";

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

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var timedOut = false;
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            timedOut = true;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
            }
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
