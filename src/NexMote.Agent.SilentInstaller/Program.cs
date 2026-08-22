using System.Diagnostics;
using System.Runtime.Versioning;

if (!OperatingSystem.IsWindows())
{
    return 1;
}

Run();
return 0;

[SupportedOSPlatform("windows")]
static void Run()
{
    var baseDir = AppContext.BaseDirectory;
    var msiPath = Path.Combine(baseDir, "NexMote-Agent-Setup.msi");
    var logDir = Path.Combine(baseDir, "logs");
    Directory.CreateDirectory(logDir);
    var logPath = Path.Combine(logDir, $"silent-install-{DateTime.Now:yyyyMMdd-HHmmss}.log");

    if (!File.Exists(msiPath))
    {
        WriteLog(logPath, $"MSI bulunamadi: {msiPath}");
        return;
    }

    // UAC acikken ProcessStartInfo.UserName/Password ile farkli kullanici olarak baslatilan
    // sureçler, o hesap admin olsa bile FILTRELENMIS (standart) token ile calisir - bu yuzden
    // msiexec yine elevation icin sifre sorar. Gercekten sessiz/promptsuz calismasi icin
    // Zamanlanmis Gorev + "En yuksek yetkiyle calistir" (RL HIGHEST) kullanilir - bu, UAC token
    // filtrelemesini atlayan resmi/desteklenen tek Windows mekanizmasidir.
    var taskName = $"NexMoteBootstrap-{Guid.NewGuid():N}"[..24];
    var msiexecLog = Path.Combine(logDir, $"msiexec-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    var runAsUser = $"{Credentials.Domain}\\{Credentials.Username}";
    var taskCommand = $"msiexec.exe /i \"{msiPath}\" /quiet /norestart /l*v \"{msiexecLog}\"";

    try
    {
        WriteLog(logPath, $"Zamanlanmis gorev olusturuluyor: {taskName} (kullanici: {runAsUser})");
        RunSchtasks(logPath,
            "/Create", "/TN", taskName, "/TR", taskCommand, "/SC", "ONCE",
            "/ST", "00:00", "/RU", runAsUser, "/RP", Credentials.Password, "/RL", "HIGHEST", "/F");

        WriteLog(logPath, "Gorev calistiriliyor...");
        RunSchtasks(logPath, "/Run", "/TN", taskName);

        WaitForTaskCompletion(logPath, taskName);
    }
    finally
    {
        RunSchtasks(logPath, "/Delete", "/TN", taskName, "/F");
    }

    WriteLog(logPath, "Islem tamamlandi.");
}

[SupportedOSPlatform("windows")]
static void WaitForTaskCompletion(string logPath, string taskName)
{
    var deadline = DateTime.Now.AddSeconds(180);
    while (DateTime.Now < deadline)
    {
        Thread.Sleep(3000);
        var status = QueryTaskStatus(taskName);
        if (status is not null && !status.Contains("Running", StringComparison.OrdinalIgnoreCase))
        {
            WriteLog(logPath, $"Gorev durumu: {status}");
            return;
        }
    }

    WriteLog(logPath, "Gorev zaman asimina ugradi (180sn), yine de siliniyor.");
}

[SupportedOSPlatform("windows")]
static string? QueryTaskStatus(string taskName)
{
    var psi = new ProcessStartInfo
    {
        FileName = "schtasks.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    psi.ArgumentList.Add("/Query");
    psi.ArgumentList.Add("/TN");
    psi.ArgumentList.Add(taskName);
    psi.ArgumentList.Add("/FO");
    psi.ArgumentList.Add("LIST");

    try
    {
        using var process = Process.Start(psi);
        if (process is null) return null;
        var stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit(15_000);
        var statusLine = stdout
            .Split('\n')
            .FirstOrDefault(l => l.TrimStart().StartsWith("Status:", StringComparison.OrdinalIgnoreCase));
        return statusLine?.Trim();
    }
    catch
    {
        return null;
    }
}

[SupportedOSPlatform("windows")]
static void RunSchtasks(string logPath, params string[] args)
{
    var psi = new ProcessStartInfo
    {
        FileName = "schtasks.exe",
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (var a in args)
    {
        psi.ArgumentList.Add(a);
    }

    try
    {
        using var process = Process.Start(psi);
        if (process is null)
        {
            WriteLog(logPath, "schtasks baslatilamadi.");
            return;
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30_000);

        // Parola loga ASLA yazilmaz - /RP degerinden sonraki argumani redakte et.
        var redacted = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            redacted.Add(args[i]);
            if (string.Equals(args[i], "/RP", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                redacted.Add("***");
                i++;
            }
        }

        WriteLog(logPath, $"schtasks {string.Join(' ', redacted)} -> exit {process.ExitCode}");
        if (!string.IsNullOrWhiteSpace(stderr))
        {
            WriteLog(logPath, $"  stderr: {stderr.Trim()}");
        }
    }
    catch (Exception ex)
    {
        WriteLog(logPath, $"schtasks calistirilamadi: {ex.Message}");
    }
}

static void WriteLog(string path, string message)
{
    try
    {
        File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
    catch
    {
        // Log yazilamazsa sessizce devam et - kurulumu engellememeli.
    }
}
