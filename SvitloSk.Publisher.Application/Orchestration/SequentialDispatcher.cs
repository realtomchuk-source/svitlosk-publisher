using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Application.Orchestration;

/// <summary>
/// Backward-compatible dispatcher bridge implementing Clean Architecture port IChannelPipeline.
/// Allows PublisherOrchestrator to dispatch decisions polymorphically across channels.
/// </summary>
public class SequentialDispatcher : IChannelPipeline
{
    private readonly ITelegramAdapter _telegramAdapter;
    private readonly IDelayProvider _delayProvider;
    private readonly string? _defaultChatId;
    private readonly string? _defaultDiscussionGroupId;

    public string ChannelName => "Telegram";

    public SequentialDispatcher(ITelegramAdapter telegramAdapter, IDelayProvider delayProvider, string? defaultChatId = null, string? defaultDiscussionGroupId = null)
    {
        _telegramAdapter = telegramAdapter ?? throw new ArgumentNullException(nameof(telegramAdapter));
        _delayProvider = delayProvider ?? throw new ArgumentNullException(nameof(delayProvider));
        _defaultChatId = defaultChatId;
        _defaultDiscussionGroupId = defaultDiscussionGroupId;
    }

    public Task<BatchDispatchResult> DispatchAsync(IReadOnlyList<EditorialDecision> decisions, CancellationToken cancellationToken = default)
    {
        string chat = _defaultChatId ?? "-100123";
        return DispatchAsync(chat, decisions, _defaultDiscussionGroupId, cancellationToken);
    }

    public async Task<BatchDispatchResult> DispatchAsync(
        string chatNameOrId,
        IReadOnlyList<EditorialDecision> decisions,
        string? discussionGroupId = null,
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
            if (totalProcessed > 0 && IsTelegramOperation(decision.DecisionResult))
            {
                await _delayProvider.DelayAsync(1000, cancellationToken).ConfigureAwait(false);
            }

            if (!IsTelegramOperation(decision.DecisionResult))
            {
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

                if (adapterResult.IsSuccess && decision.DecisionResult == DecisionResult.Create && adapterResult.MessageId.HasValue && !string.IsNullOrWhiteSpace(discussionGroupId))
                {
                    try
                    {
                        await _telegramAdapter.CloseCommentsAsync(discussionGroupId, adapterResult.MessageId.Value, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception closeEx) when (closeEx is not OperationCanceledException)
                    {
                        Console.Error.WriteLine($"[WARN] Could not close comments in discussion group for msg {adapterResult.MessageId.Value}: {closeEx.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
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
                adapterResult.IsSuccess ? decision.TargetHash : adapterResult.ErrorDescription
            ));

            if (adapterResult.IsSuccess)
            {
                totalSuccessful++;
            }
            else
            {
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
                    result = await _telegramAdapter.SendAsync(chatNameOrId, decision.TargetHash ?? "Create content", decision.GraphicBytes, cancellationToken).ConfigureAwait(false);
                    break;

                case DecisionResult.Update:
                    if (decision.PublicationId == null)
                        throw new InvalidOperationException("Cannot update publication without Guid/ID identifier.");
                    if (decision.TelegramMessageId == null)
                        throw new InvalidOperationException("Cannot update publication without its Telegram message ID.");
                    result = await _telegramAdapter.UpdateAsync(chatNameOrId, decision.TelegramMessageId.Value, decision.TargetHash ?? "Update content", decision.GraphicBytes, cancellationToken).ConfigureAwait(false);
                    break;

                case DecisionResult.Delete:
                    if (decision.TelegramMessageId == null)
                        throw new InvalidOperationException("Cannot delete publication without its Telegram message ID.");
                    result = await _telegramAdapter.DeleteAsync(chatNameOrId, decision.TelegramMessageId.Value, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported Telegram operation result: {decision.DecisionResult}");
            }

            if (result.IsSuccess || !result.IsRetryable || attempt >= maxAttempts)
            {
                return result;
            }

            int delayMs;
            if (result.RetryAfterSeconds.HasValue)
            {
                int delaySec = result.RetryAfterSeconds.Value;
                if (delaySec > 60) delaySec = 60;
                delayMs = delaySec * 1000;
            }
            else
            {
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
