using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ResolveIQ.Api.Services;

public class SlaMonitoringBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SlaMonitoringBackgroundService> _logger;

    public SlaMonitoringBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<SlaMonitoringBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue<bool>("SlaMonitoring:Enabled", true);
        if (!enabled)
        {
            _logger.LogInformation("SlaMonitoringBackgroundService is disabled by configuration.");
            return;
        }

        var intervalSeconds = _configuration.GetValue<int>("SlaMonitoring:IntervalSeconds", 60);
        var interval = TimeSpan.FromSeconds(Math.Max(5, intervalSeconds));

        _logger.LogInformation(
            "SlaMonitoringBackgroundService started with interval {IntervalSeconds}s.",
            interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var monitoringService = scope.ServiceProvider.GetRequiredService<ISlaMonitoringService>();
                    await monitoringService.EvaluateDeadlinesAsync(stoppingToken);
                }

                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in SLA monitoring loop.");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
