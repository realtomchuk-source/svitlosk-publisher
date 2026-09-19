using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Backward-compatible dispatcher bridge implementing Clean Architecture port IChannelPipeline.
/// Encapsulates direct Telegram dispatch operations for legacy or unit-test scenarios.
/// </summary>
public class SequentialDispatcher : IChannelPipeline
{
    private readonly ITelegramAdapter _telegramAdapter;
    private readonly IDelayProvider _delayProvider;
    private readonly string? _defaultChatId;
    private readonly string? _defaultDiscussionGroupId;
    private readonly IGraphicPublisherDispatcher? _graphicDispatcher;

    public string ChannelName => "Telegram";

    public SequentialDispatcher(
        ITelegramAdapter telegramAdapter,
        IDelayProvider delayProvider,
        string? defaultChatId = null,
        string? defaultDiscussionGroupId = null,
        IGraphicPublisherDispatcher? graphicDispatcher = null)
    {
        _telegramAdapter = telegramAdapter ?? throw new ArgumentNullException(nameof(telegramAdapter));
        _delayProvider = delayProvider ?? throw new ArgumentNullException(nameof(delayProvider));
        _defaultChatId = defaultChatId;
        _defaultDiscussionGroupId = defaultDiscussionGroupId;
        _graphicDispatcher = graphicDispatcher;
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
                    ErrorDescription: null,
                    PublicationType: decision.Type.ToString()
                ));
                totalSuccessful++;
                continue;
            }

            totalProcessed++;
            TelegramDispatchResult adapterResult;

            try
            {
                if (decision.Type == PublicationType.Graphic)
                {
                    if (_graphicDispatcher != null)
                    {
                        var graphicPayload = new GraphicOperationPayload(
                            ChatNameOrId: chatNameOrId,
                            OperationType: decision.DecisionResult.ToString().ToUpperInvariant(),
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
                        adapterResult = new TelegramDispatchResult(true, decision.TelegramMessageId, null, false);
                    }
                }
                else
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
                    ErrorDescription: desc,
                    PublicationType: decision.Type.ToString()
                ));
                return new BatchDispatchResult(false, totalProcessed, totalSuccessful, desc, results);
            }

            results.Add(new DispatchResultRecord(
                decision.PublicationId,
                decision.TerritoryIdentifier,
                decision.DecisionResult.ToString(),
                adapterResult.IsSuccess,
                adapterResult.MessageId,
                adapterResult.IsSuccess ? decision.TargetHash : adapterResult.ErrorDescription,
                decision.Type.ToString(),
                adapterResult.MessageId?.ToString() ?? decision.ExternalMessageId
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
        int maxAttempts = 3;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TelegramDispatchResult result = decision.DecisionResult switch
            {
                DecisionResult.Create => await _telegramAdapter.SendAsync(chatNameOrId, decision.TargetHash ?? "Create content", decision.GraphicBytes, cancellationToken).ConfigureAwait(false),
                DecisionResult.Update => await _telegramAdapter.UpdateAsync(chatNameOrId, decision.TelegramMessageId ?? 0, decision.TargetHash ?? "Update content", decision.GraphicBytes, cancellationToken).ConfigureAwait(false),
                DecisionResult.Delete => await _telegramAdapter.DeleteAsync(chatNameOrId, decision.TelegramMessageId ?? 0, cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException($"Unsupported dispatch operation: {decision.DecisionResult}")
            };

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

        return new TelegramDispatchResult(false, null, "Exhausted all retry attempts.", false);
    }
}
