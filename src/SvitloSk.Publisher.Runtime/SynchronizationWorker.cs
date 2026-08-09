using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace SvitloSk.Publisher.Runtime;

public class SynchronizationWorker : BackgroundService
{
    private readonly ILogger<SynchronizationWorker> _logger;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public SynchronizationWorker(
        ILogger<SynchronizationWorker> logger,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runtime started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var synchronizationEngine = scope.ServiceProvider.GetRequiredService<ISynchronizationEngine>();
                await synchronizationEngine.MaintainPublisherStateAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Synchronization failed");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Runtime stopping");
    }
}
