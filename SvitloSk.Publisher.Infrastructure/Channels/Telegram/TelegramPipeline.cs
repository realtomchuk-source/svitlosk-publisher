using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Dedicated Telegram Channel Pipeline per Clean Architecture port IChannelPipeline.
/// Encapsulates platform-specific dispatch sequencing, rate limiting, comment closure,
/// tail-positioning invariants (system_status), and 12-subqueue graphic dispatching.
/// </summary>
public class TelegramPipeline : IChannelPipeline
{
    private readonly ITelegramAdapter _telegramAdapter;
    private readonly IGraphicPublisherDispatcher? _graphicDispatcher;
    private readonly TelegramRateLimiter _rateLimiter;
    private readonly TelegramDiscussionManager? _discussionManager;
    private readonly string _chatId;
    private readonly string? _discussionGroupId;

    public string ChannelName => "Telegram";

    public TelegramPipeline(
        ITelegramAdapter telegramAdapter,
        string chatId,
        string? discussionGroupId = null,
        IGraphicPublisherDispatcher? graphicDispatcher = null,
        TelegramRateLimiter? rateLimiter = null,
        TelegramDiscussionManager? discussionManager = null)
    {
        _telegramAdapter = telegramAdapter ?? throw new ArgumentNullException(nameof(telegramAdapter));
        _chatId = !string.IsNullOrWhiteSpace(chatId) ? chatId : throw new ArgumentException("Chat ID cannot be null or empty.", nameof(chatId));
        _discussionGroupId = discussionGroupId;
        _graphicDispatcher = graphicDispatcher;
        _rateLimiter = rateLimiter ?? new TelegramRateLimiter();
        _discussionManager = discussionManager;
    }

    public async Task<BatchDispatchResult> DispatchAsync(
        IReadOnlyList<EditorialDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        if (decisions == null)
            throw new ArgumentNullException(nameof(decisions));

        var results = new List<DispatchResultRecord>();
        int totalProcessed = 0;
        int totalSuccessful = 0;

        for (int i = 0; i < decisions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = decisions[i];

            // Throttling: 1000ms delay between consecutive Telegram operations.
            if (totalProcessed > 0 && IsTelegramOperation(decision.DecisionResult))
            {
                await _rateLimiter.ThrottleAsync(cancellationToken).ConfigureAwait(false);
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
                // Route Graphic schedule decisions to specialized TelegramGraphicPublisherDispatcher
                if (decision.Type == PublicationType.Graphic && _graphicDispatcher != null)
                {
                    var graphicPayload = new GraphicOperationPayload(
                        ChatNameOrId: _chatId,
                        OperationType: decision.DecisionResult.ToString(),
                        TerritoryId: decision.TerritoryIdentifier ?? "Старокостянтинівська МТГ",
                        ContentHash: decision.TargetHash ?? "graphic-hash",
                        SvgBytes: decision.SvgBytes,
                        ExternalMessageId: decision.ExternalMessageId,
                        ScheduleDate: decision.ScheduleDate
                    );

                    adapterResult = await _graphicDispatcher.DispatchGraphicAsync(graphicPayload, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    adapterResult = await ExecuteWithRetryPolicyAsync(_chatId, decision, cancellationToken).ConfigureAwait(false);

                    // If this was a successful CREATE for a post and a discussionGroupId is configured,
                    // close comments by deleting the auto-forwarded message in the discussion group.
                    if (adapterResult.IsSuccess && decision.DecisionResult == DecisionResult.Create && adapterResult.MessageId.HasValue && !string.IsNullOrWhiteSpace(_discussionGroupId))
                    {
                        try
                        {
                            if (_discussionManager != null)
                            {
                                await _discussionManager.CloseCommentsAsync(_discussionGroupId, adapterResult.MessageId.Value, cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await _telegramAdapter.CloseCommentsAsync(_discussionGroupId, adapterResult.MessageId.Value, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (Exception closeEx) when (closeEx is not OperationCanceledException)
                        {
                            // Fail-safe: Comment closing error in discussion group must not break channel publishing
                            Console.Error.WriteLine($"[WARN] Could not close comments in discussion group for msg {adapterResult.MessageId.Value}: {closeEx.Message}");
                        }
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

            await _rateLimiter.DelayBackoffAsync(attempt, result.RetryAfterSeconds, cancellationToken).ConfigureAwait(false);
        }
    }
}
