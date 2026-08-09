using NexMote.Agent.Windows;
using NexMote.Agent.Windows.Logging;

var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "NexMote",
    "Logs");
var logPath = Path.Combine(logDir, "agent-service.log");

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.AddProvider(new FileLoggerProvider(logPath));

    builder.Services.AddWindowsService(options =>
    {
        options.ServiceName = "NexMote Agent";
    });

    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));
    builder.Services.AddHttpClient<AgentClient>();
    builder.Services.AddSingleton<DeviceIdentityStore>();
    builder.Services.AddHostedService<Worker>();

    using var host = builder.Build();
    host.Run();
}
catch (Exception exception)
{
    try
    {
        Directory.CreateDirectory(logDir);
        File.AppendAllText(
            Path.Combine(logDir, "agent-service-startup-error.log"),
            $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        // The fallback log must never prevent the service from reporting its original error.
    }

    throw;
}
