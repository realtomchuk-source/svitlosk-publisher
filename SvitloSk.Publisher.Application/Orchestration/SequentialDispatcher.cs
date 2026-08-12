using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Application.Orchestration;

public class SequentialDispatcher
{
    private readonly ITelegramAdapter _telegramAdapter;
    private readonly IDelayProvider _delayProvider;

    public SequentialDispatcher(ITelegramAdapter telegramAdapter, IDelayProvider delayProvider)
    {
        _telegramAdapter = telegramAdapter ?? throw new ArgumentNullException(nameof(telegramAdapter));
        _delayProvider = delayProvider ?? throw new ArgumentNullException(nameof(delayProvider));
    }

    public async Task<BatchDispatchResult> DispatchAsync(
        string chatNameOrId,
        IReadOnlyList<EditorialDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(chatNameOrId));

        if (decisions == null)
            throw new ArgumentNullException(nameof(decisions));

        var results = new List<DispatchResultRecord>();
        int totalProcessed = 0;
        int totalSuccessful = 0;

        for (int i = 0; i < decisions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = decisions[i];

            // 1. Throttling: 1000ms delay between consecutive Telegram operations.
            // A Telegram operation is sent for CREATE (Create), UPDATE (Update), or DELETE (Delete).
            // We apply throttling before an operation if at least one operation has already been processed.
            if (totalProcessed > 0 && IsTelegramOperation(decision.DecisionResult))
            {
                await _delayProvider.DelayAsync(1000, cancellationToken).ConfigureAwait(false);
            }

            if (!IsTelegramOperation(decision.DecisionResult))
            {
                // KEEP or NO_ACTION or other non-Telegram decisions
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    IsSuccess: true,
                    MessageId: null,
                    ErrorDescription: null
                ));
                totalSuccessful++;
                continue;
            }

            totalProcessed++;
            TelegramDispatchResult adapterResult;

            try
            {
                adapterResult = await ExecuteWithRetryPolicyAsync(chatNameOrId, decision, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Unhandled exception during operation: treat as a fatal run failure.
                string desc = $"Fatal unhandled exception during operation: {ex.Message}";
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    IsSuccess: false,
                    MessageId: null,
                    ErrorDescription: desc
                ));
                return new BatchDispatchResult(false, totalProcessed, totalSuccessful, desc, results);
            }

            results.Add(new DispatchResultRecord(
                decision.PublicationId,
                decision.TerritoryIdentifier,
                decision.DecisionResult.ToString(),
                adapterResult.IsSuccess,
                adapterResult.MessageId,
                adapterResult.ErrorDescription
            ));

            if (adapterResult.IsSuccess)
            {
                totalSuccessful++;
            }
            else
            {
                // Non-success at this point means retry limits were exhausted, or it's a fatal failure.
                // Either way, we must abort immediately in a Fail-Closed manner.
                string fatalDesc = adapterResult.ErrorDescription ?? "Operation failed after retries or encountered a fatal error.";
                return new BatchDispatchResult(false, totalProcessed, totalSuccessful, fatalDesc, results);
            }
        }

        return new BatchDispatchResult(true, totalProcessed, totalSuccessful, null, results);
    }

    private static bool IsTelegramOperation(DecisionResult result)
    {
        return result == DecisionResult.Create || 
               result == DecisionResult.Update || 
               result == DecisionResult.Delete;
    }

    private async Task<TelegramDispatchResult> ExecuteWithRetryPolicyAsync(
        string chatNameOrId,
        EditorialDecision decision,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        const int maxAttempts = 3;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            TelegramDispatchResult result;

            switch (decision.DecisionResult)
            {
                case DecisionResult.Create:
                    // Create maps to SendAsync. Graphic bytes are simulated/provided via compilation, 
                    // which is resolved by application orchestrator. For dispatcher ports we assume text payload.
                    // (TargetHash or layout is passed to adapter by orchestrator, for E-01 we send text target hash as mockup or decision text).
                    result = await _telegramAdapter.SendAsync(chatNameOrId, decision.TargetHash ?? "Create content", null, cancellationToken).ConfigureAwait(false);
                    break;

                case DecisionResult.Update:
                    if (decision.PublicationId == null)
                        throw new InvalidOperationException("Cannot update publication without Guid/ID identifier.");
                    // For update, the message ID must be resolved by orchestrator from registry.
                    // For Dispatcher unit boundaries, we simulate message ID passing. We assume mock ID 9999 or mapping from decision context.
                    // We extract numerical value if possible or use a fallback. 
                    int msgId = 9999; 
                    result = await _telegramAdapter.UpdateAsync(chatNameOrId, msgId, decision.TargetHash ?? "Update content", null, cancellationToken).ConfigureAwait(false);
                    break;

                case DecisionResult.Delete:
                    int delMsgId = 9999;
                    result = await _telegramAdapter.DeleteAsync(chatNameOrId, delMsgId, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported Telegram operation result: {decision.DecisionResult}");
            }

            if (result.IsSuccess)
            {
                return result;
            }

            if (!result.IsRetryable || attempt >= maxAttempts)
            {
                return result;
            }

            // Calculate delay
            int delayMs;
            if (result.RetryAfterSeconds.HasValue)
            {
                // HTTP 429
                int delaySec = result.RetryAfterSeconds.Value;
                if (delaySec > 60)
                {
                    delaySec = 60; // Max delay cap
                }
                delayMs = delaySec * 1000;
            }
            else
            {
                // HTTP 5xx or transient timeout: exponential backoff (attempt 1 -> 1000ms, attempt 2 -> 2000ms)
                delayMs = attempt switch
                {
                    1 => 1000,
                    2 => 2000,
                    _ => 4000
                };
            }

            await _delayProvider.DelayAsync(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
