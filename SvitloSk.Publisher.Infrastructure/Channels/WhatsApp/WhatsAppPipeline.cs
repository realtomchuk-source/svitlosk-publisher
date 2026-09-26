using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// Dedicated WhatsApp Channel Pipeline implementing Clean Architecture port IChannelPipeline.
/// Encapsulates platform-specific WhatsApp message sequencing, guaranteed priority for the city
/// of Starokostiantyniv (*м. СТАРОКОСТЯНТИНІВ*), intelligent 30-minute update/rollover window,
/// rate-limiting, and ephemeral tail invariant (system_status).
/// </summary>
public class WhatsAppPipeline : IChannelPipeline
{
    private readonly IWhatsAppAdapter _whatsappAdapter;
    private readonly string _channelId;
    private readonly WhatsAppRateLimiter _rateLimiter;

    public string ChannelName => "WhatsApp";

    public WhatsAppPipeline(
        IWhatsAppAdapter whatsappAdapter,
        string channelId,
        WhatsAppRateLimiter? rateLimiter = null)
    {
        _whatsappAdapter = whatsappAdapter ?? throw new ArgumentNullException(nameof(whatsappAdapter));
        _channelId = !string.IsNullOrWhiteSpace(channelId) ? channelId : throw new ArgumentException("WhatsApp Channel ID cannot be null or empty.", nameof(channelId));
        _rateLimiter = rateLimiter ?? new WhatsAppRateLimiter();
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

        // Order decisions strictly: Rollover -> Journal Header -> City (Priority #1) -> Okruhs -> Tomorrow -> Graphic -> System Status
        var orderedDecisions = OrderDecisionsWithCityPriority(decisions);

        for (int i = 0; i < orderedDecisions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decision = orderedDecisions[i];

            // Pacing / Rate limiting between consecutive operations
            if (totalProcessed > 0 && IsWhatsAppOperation(decision.DecisionResult))
            {
                await _rateLimiter.ThrottleAsync(cancellationToken).ConfigureAwait(false);
            }

            // Ephemeral tomorrow forecasts are not posted to WhatsApp Channels
            // because WhatsApp Channels (Newsletters) do not support automated message deletion for previews.
            // Only the official daily journal is posted at the start of the current day.
            if (decision.TerritoryIdentifier != null && decision.TerritoryIdentifier.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    IsSuccess: true,
                    MessageId: null,
                    ErrorDescription: null,
                    PublicationType: decision.Type.ToString(),
                    ExternalMessageId: decision.ExternalMessageId ?? "wa_virtual_tomorrow"
                ));
                totalSuccessful++;
                continue;
            }

            if (!IsWhatsAppOperation(decision.DecisionResult))
            {
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    IsSuccess: true,
                    MessageId: null,
                    ErrorDescription: null,
                    PublicationType: decision.Type.ToString(),
                    ExternalMessageId: decision.ExternalMessageId
                ));
                totalSuccessful++;
                continue;
            }

            totalProcessed++;
            WhatsAppDispatchResult adapterResult;

            try
            {
                adapterResult = await ExecuteWithRetryPolicyAsync(_channelId, decision, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string desc = $"Fatal unhandled exception during WhatsApp dispatch: {ex.Message}";
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    IsSuccess: false,
                    MessageId: null,
                    ErrorDescription: desc,
                    PublicationType: decision.Type.ToString(),
                    ExternalMessageId: null
                ));
                return new BatchDispatchResult(false, totalProcessed, totalSuccessful, desc, results);
            }

            results.Add(new DispatchResultRecord(
                decision.PublicationId,
                decision.TerritoryIdentifier,
                decision.DecisionResult.ToString(),
                adapterResult.IsSuccess,
                adapterResult.MessageId != null && int.TryParse(adapterResult.MessageId, out int mid) ? mid : null,
                adapterResult.IsSuccess ? decision.TargetHash : adapterResult.ErrorDescription,
                decision.Type.ToString(),
                adapterResult.MessageId ?? decision.ExternalMessageId
            ));

            if (adapterResult.IsSuccess)
            {
                totalSuccessful++;
            }
            else
            {
                string fatalDesc = adapterResult.ErrorDescription ?? "WhatsApp operation failed after retries.";
                return new BatchDispatchResult(false, totalProcessed, totalSuccessful, fatalDesc, results);
            }
        }

        return new BatchDispatchResult(true, totalProcessed, totalSuccessful, null, results);
    }

    private static bool IsWhatsAppOperation(DecisionResult result)
    {
        return result == DecisionResult.Create ||
               result == DecisionResult.Update ||
               result == DecisionResult.Delete;
    }

    private async Task<WhatsAppDispatchResult> ExecuteWithRetryPolicyAsync(
        string channelId,
        EditorialDecision decision,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        const int maxAttempts = 3;

        string formattedContent = WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(decision.TargetHash ?? string.Empty);

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            WhatsAppDispatchResult result;

            switch (decision.DecisionResult)
            {
                case DecisionResult.Create:
                    if (decision.GraphicBytes != null && decision.GraphicBytes.Length > 0)
                    {
                        if (string.Equals(decision.TerritoryIdentifier, "journal_header", StringComparison.OrdinalIgnoreCase))
                        {
                            // Decouple the static daily banner and the mutable text summary:
                            // 1. Post the static image banner first.
                            await _whatsappAdapter.SendMediaMessageAsync(channelId, string.Empty, decision.GraphicBytes, "image/png", cancellationToken).ConfigureAwait(false);
                            // 2. Post the text summary directly beneath it and record the text message ID for future in-place edits.
                            result = await _whatsappAdapter.SendTextMessageAsync(channelId, formattedContent, cancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            result = await _whatsappAdapter.SendMediaMessageAsync(channelId, formattedContent, decision.GraphicBytes, "image/png", cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        result = await _whatsappAdapter.SendTextMessageAsync(channelId, formattedContent, cancellationToken).ConfigureAwait(false);
                    }
                    break;

                case DecisionResult.Update:
                    string? existingId = decision.ExternalMessageId;
                    if (string.IsNullOrEmpty(existingId))
                    {
                        // Fallback to Create if no previous ID exists
                        result = await _whatsappAdapter.SendTextMessageAsync(channelId, formattedContent, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        // Attempt safe in-place Update
                        result = await _whatsappAdapter.UpdateTextMessageAsync(channelId, existingId, formattedContent, cancellationToken).ConfigureAwait(false);

                        // If Update fails (e.g. expired 30-minute window or API unsupported edit), fallback to Delete + Send rollover
                        if (!result.IsSuccess)
                        {
                            Console.WriteLine($"[INFO][WhatsApp] In-place edit for '{existingId}' failed ({result.ErrorDescription}). Executing Delete + Recreate rollover.");
                            await _whatsappAdapter.DeleteMessageAsync(channelId, existingId, cancellationToken).ConfigureAwait(false);
                            result = await _whatsappAdapter.SendTextMessageAsync(channelId, formattedContent, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    break;

                case DecisionResult.Delete:
                    if (string.IsNullOrEmpty(decision.ExternalMessageId))
                    {
                        result = new WhatsAppDispatchResult(true, null, "No external message ID to delete (No-Op)", false);
                    }
                    else
                    {
                        result = await _whatsappAdapter.DeleteMessageAsync(channelId, decision.ExternalMessageId, cancellationToken).ConfigureAwait(false);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported WhatsApp decision result: {decision.DecisionResult}");
            }

            if (result.IsSuccess || !result.IsRetryable || attempt >= maxAttempts)
            {
                return result;
            }

            await _rateLimiter.DelayBackoffAsync(attempt, result.RetryAfterSeconds, cancellationToken).ConfigureAwait(false);
        }
    }

    public static List<EditorialDecision> OrderDecisionsWithCityPriority(IReadOnlyList<EditorialDecision> decisions)
    {
        return decisions
            .OrderBy(d => GetDecisionPriorityRank(d))
            .ThenBy(d => d.TerritoryIdentifier ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static int GetDecisionPriorityRank(EditorialDecision d)
    {
        if (d.DecisionResult == DecisionResult.Delete && d.Classification == PublicationClassification.Ephemeral)
            return 0; // Rollover cleanups first

        string territory = d.TerritoryIdentifier ?? string.Empty;

        if (territory.Equals("journal_header", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (territory.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) ||
            (territory.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) && !territory.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase)))
            return 2; // GUARANTEED PRIORITY #1: Administrative Center *м. СТАРОКОСТЯНТИНІВ*!

        if (!territory.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) &&
            !territory.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
            d.Type != PublicationType.Graphic)
            return 3; // Rural okruhs

        if (territory.Equals("tomorrow_separator", StringComparison.OrdinalIgnoreCase))
            return 4;

        if (territory.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
            return 5;

        if (d.Type == PublicationType.Graphic)
            return 6;

        if (territory.Equals("system_status", StringComparison.OrdinalIgnoreCase))
            return 7; // ABSOLUTE TAIL!

        return 8;
    }
}
