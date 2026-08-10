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
    private readonly string _workerId;

    public OutboxDispatcherWorker(
        ILogger<OutboxDispatcherWorker> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _workerId = Guid.NewGuid().ToString("N");
    }

    private static readonly SemaphoreSlim _dispatchSemaphore = new(1, 1);
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherWorker {WorkerId} started.", _workerId);

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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing outbox messages.");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("OutboxDispatcherWorker {WorkerId} stopping.", _workerId);
    }

    private async Task ProcessOutboxMessagesAsync(CancellationToken cancellationToken)
    {
        System.Collections.Generic.IReadOnlyCollection<OutboxMessage> claimedMessages;

        using (var claimScope = _scopeFactory.CreateScope())
        {
            var outboxRepo = claimScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var claimUnitOfWork = claimScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            
            claimedMessages = outboxRepo.ClaimMessages(10, _workerId, TimeSpan.FromMinutes(2));
            if (!claimedMessages.Any())
                return;
                
            await claimUnitOfWork.CommitAsync(cancellationToken);
        }

        using (var dispatchScope = _scopeFactory.CreateScope())
        {
            var pipeline = dispatchScope.ServiceProvider.GetRequiredService<IPublicationPipeline>();
            var identityResolver = dispatchScope.ServiceProvider.GetRequiredService<IExternalPublicationIdentityResolver>();
            var outboxRepo = dispatchScope.ServiceProvider.GetRequiredService<IOutboxRepository>();
            var unitOfWork = dispatchScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            foreach (var claimed in claimedMessages)
            {
                // Reload message in current scope
                var message = outboxRepo.GetById(claimed.OperationId);
                if (message == null || message.ClaimedBy != _workerId) continue;

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
                        identityResolver.RemoveExternalIdentity(message.PublicationId);
                    }

                    await unitOfWork.CommitAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch OutboxMessage {OperationId}", message.OperationId);
                    
                    message.AttemptCount++;
                    message.LastError = ex.Message;
                    
                    if (ex is InvalidOperationException && ex.Message.Contains("Telegram API returned ok=false"))
                    {
                         message.Status = OutboxOperationStatus.Failed;
                    }
                    else if (message.AttemptCount >= MaxAttempts)
                    {
                         message.Status = OutboxOperationStatus.Failed;
                         _logger.LogError("Message {OperationId} reached MaxAttempts ({MaxAttempts}) and is marked as Failed.", message.OperationId, MaxAttempts);
                    }
                    else
                    {
                         TimeSpan backoff = TimeSpan.FromSeconds(Math.Pow(2, message.AttemptCount));
                         if (ex is RetryableTransportException rtex && rtex.RetryAfter.HasValue)
                         {
                             backoff = rtex.RetryAfter.Value;
                         }
                         message.NextRetryAt = DateTimeOffset.UtcNow.Add(backoff);
                         message.Status = OutboxOperationStatus.Pending;
                    }

                    await unitOfWork.CommitAsync(cancellationToken);
                }
            }
        }
    }
}
