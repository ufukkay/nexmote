using Microsoft.Extensions.Options;
using System.Net;
using System.Text.Json;

namespace NexMote.Agent.Windows;

public sealed class Worker : BackgroundService
{
    private readonly AgentClient _client;
    private readonly DeviceIdentityStore _identityStore;
    private readonly ILogger<Worker> _logger;
    private readonly AgentOptions _options;

    public Worker(
        AgentClient client,
        DeviceIdentityStore identityStore,
        IOptions<AgentOptions> options,
        ILogger<Worker> logger)
    {
        _client = client;
        _identityStore = identityStore;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DeviceIdentity? identity = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (identity is null)
                {
                    identity = await _identityStore.LoadAsync(stoppingToken);

                    if (identity is null)
                    {
                        _logger.LogInformation("Enrolling NexMote device.");
                        identity = await _client.EnrollAsync(stoppingToken);
                        await _identityStore.SaveAsync(identity, stoppingToken);
                        _logger.LogInformation("Enrollment completed for {DeviceId}.", identity.DeviceId);
                    }
                }

                await _client.SendHeartbeatAsync(identity, stoppingToken);
                _logger.LogInformation("Heartbeat sent for {DeviceId}.", identity.DeviceId);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized)
            {
                _logger.LogWarning(ex, "Heartbeat identity rejected. Re-enrolling device.");
                _identityStore.Delete();
                identity = null;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Stored device identity is invalid. It will be replaced.");
                _identityStore.Delete();
                identity = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Agent loop failed. NexMote will retry.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.HeartbeatSeconds), stoppingToken);
        }
    }
}
