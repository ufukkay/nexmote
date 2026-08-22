using NexMote.Agent.Windows;
using NexMote.Agent.Windows.Logging;
using NexMote.Shared.Identity;
using NexMote.Shared.Telemetry;

// Windows Servisi günlük (log) dizini: %ProgramData%\NexMote\Logs\agent-service.log
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "NexMote",
    "Logs");
var logPath = Path.Combine(logDir, "agent-service.log");

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Dosyaya loglama sağlayıcısını ekle
    builder.Logging.AddProvider(new FileLoggerProvider(logPath));

    // Windows Background Service olarak yapılandır
    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "NexMote Agent";
    });

    // %ProgramData%\NexMote\Agent\appsettings.json konumundaki dinamik yapılandırmayı bağla
    var programDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NexMote", "Agent");
    Directory.CreateDirectory(programDataDir);
    var programDataConfig = Path.Combine(programDataDir, "appsettings.json");
    builder.Configuration.AddJsonFile(programDataConfig, optional: true, reloadOnChange: true);

    // Bağımlılık enjeksiyonları (DI)
    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
    builder.Services.AddSingleton<CpuUsageSampler>();
    builder.Services.AddHttpClient<AgentClient>()
        .ConfigurePrimaryHttpMessageHandler(() => NexMote.Shared.Network.NexMoteHttp.CreateHandler());
    builder.Services.AddSingleton<DeviceIdentityStore>();
    builder.Services.AddHostedService<Worker>();

    using var host = builder.Build();
    host.Run();
}
catch (Exception exception)
{
    // Servis başlangıç hatası durumunda acil durum log dosyasına yaz
    try
    {
        Directory.CreateDirectory(logDir);
        File.AppendAllText(
            Path.Combine(logDir, "agent-service-startup-error.log"),
            $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        // Yedek log yazımı ana istisnanın fırlatılmasını engellememelidir
    }

    throw;
}
