namespace NexMote.Api.Services;

/// <summary>
/// Sunucu ayakta olduğu sürece periyodik olarak (2 dakikada bir) <see cref="AlertService.EvaluateAndNotifyAsync"/>
/// çağırarak cihaz uyarılarını (çevrimdışı, disk/CPU/RAM eşik aşımı) değerlendirir ve e-posta gönderir.
/// </summary>
public sealed class AlertMonitorService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly AlertService _alerts;
    private readonly ILogger<AlertMonitorService> _logger;

    public AlertMonitorService(AlertService alerts, ILogger<AlertMonitorService> logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await _alerts.EvaluateAndNotifyAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Uyarı taraması sırasında hata oluştu, bir sonraki turda tekrar denenecek.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
