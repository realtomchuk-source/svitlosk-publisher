using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime.Persistence;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Runtime;

public class OutboxDispatcherWorker : BackgroundService
{
    private readonly ILogger<OutboxDispatcherWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public OutboxDispatcherWorker(
        ILogger<OutboxDispatcherWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    private static readonly SemaphoreSlim _dispatchSemaphore = new(1, 1);
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await _dispatchSemaphore.WaitAsync(0, stoppingToken))
                {
                    try
                    {
                        await ProcessOutboxMessagesAsync(stoppingToken);
                    }
                    finally
                    {
                        _dispatchSemaphore.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages.");
            }

            // Polling interval
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("OutboxDispatcherWorker stopping.");
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var pipeline = scope.ServiceProvider.GetRequiredService<IPublicationPipeline>();
        var identityResolver = scope.ServiceProvider.GetRequiredService<IExternalPublicationIdentityResolver>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pendingMessages = outboxRepo.GetPendingMessages(10);
        if (!pendingMessages.Any())
            return;

        // Process sequentially to guarantee order
        foreach (var message in pendingMessages)
        {
            try
            {
                var request = new PublicationRequest(
                    message.PublicationId,
                    message.Payload,
                    message.OperationType,
                    message.ExternalIdentity,
                    message.ArtifactType
                );

                var accepted = await pipeline.DispatchAsync(request, cancellationToken);

                message.Status = OutboxOperationStatus.Completed;
                message.ProcessedAt = DateTimeOffset.UtcNow;

                if (message.OperationType == TransportOperation.CREATE)
                {
                    identityResolver.RecordExternalIdentity(message.PublicationId, accepted.MessageId);
                }
                else if (message.OperationType == TransportOperation.DELETE)
                {
                    // Assuming identityResolver has a Remove method, or we implement it
                    identityResolver.RemoveExternalIdentity(message.PublicationId);
                }
                // UPDATE -> leave identity unchanged

                await unitOfWork.CommitAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch OutboxMessage {OperationId}", message.OperationId);
                
                message.AttemptCount++;
                message.LastError = ex.Message;
                
                if (ex is InvalidOperationException && ex.Message.Contains("Telegram API returned ok=false"))
                {
                     // Permanent logical failure
                     message.Status = OutboxOperationStatus.Failed;
                }
                else if (message.AttemptCount >= MaxAttempts)
                {
                     // Max retries reached
                     message.Status = OutboxOperationStatus.Failed;
                     _logger.LogError("Message {OperationId} reached MaxAttempts ({MaxAttempts}) and is marked as Failed.", message.OperationId, MaxAttempts);
                }
                else
                {
                     // Exponential backoff
                     message.NextRetryAt = DateTimeOffset.UtcNow.AddSeconds(Math.Pow(2, message.AttemptCount));
                }

                await unitOfWork.CommitAsync(cancellationToken);
            }
        }
    }
}
