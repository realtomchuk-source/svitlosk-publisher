using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SvitloSk.Publisher.Runtime;

public class SynchronizationWorker : BackgroundService
{
    private readonly ILogger<SynchronizationWorker> _logger;
    private readonly ISynchronizationEngine _synchronizationEngine;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public SynchronizationWorker(
        ILogger<SynchronizationWorker> logger,
        ISynchronizationEngine synchronizationEngine)
    {
        _logger = logger;
        _synchronizationEngine = synchronizationEngine;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Runtime started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _synchronizationEngine.MaintainPublisherStateAsync(stoppingToken);
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
