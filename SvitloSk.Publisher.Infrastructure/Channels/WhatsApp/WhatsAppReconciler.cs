using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

public record DesiredWhatsAppPost(
    string Marker,
    char MarkerType,
    string EditionDate,
    int? SequenceNumber,
    string TerritoryId,
    string CleanTextWithoutMarker,
    string FormattedTextWithMarker,
    string ContentHash,
    byte[]? GraphicBytes,
    PublicationType PublicationType
);

/// <summary>
/// Declarative State Reconciler for the SvitloSk WhatsApp Channel (Plan v6).
/// Compares expected channel tail (`DesiredWhatsAppPost` list) with the actual DOM tail (`GET /tail-state`),
/// enforces strict numbering and Variant A ordering, performs one-time transition cleanup of legacy unmarked posts,
/// and logs all audit details to system logs (`local/logs/publisher/sync.log`) without spamming Telegram.
/// </summary>
public class WhatsAppReconciler
{
    private readonly IWhatsAppAdapter _adapter;
    private readonly IRegistryStore _registryStore;
    private readonly WhatsAppPostJournalStore _journalStore;
    private readonly ContentHashCalculator _hashCalculator;
    private readonly IGraphicAssembly _graphicAssembly;
    private readonly IGraphicRasterizer _rasterizer;
    private readonly IDelayProvider _delayProvider;
    private readonly int _postDelayMs;

    public WhatsAppReconciler(
        IWhatsAppAdapter adapter,
        IRegistryStore registryStore,
        WhatsAppPostJournalStore journalStore,
        ContentHashCalculator? hashCalculator = null,
        IGraphicAssembly? graphicAssembly = null,
        IGraphicRasterizer? rasterizer = null,
        IDelayProvider? delayProvider = null,
        int postDelayMs = 5000)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _registryStore = registryStore ?? throw new ArgumentNullException(nameof(registryStore));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
        _hashCalculator = hashCalculator ?? new ContentHashCalculator();
        _graphicAssembly = graphicAssembly ?? new GraphicAssembly();
        _rasterizer = rasterizer ?? new Graphics.SvgSkiaRasterizer();
        _delayProvider = delayProvider ?? new SystemDelayProvider();
        _postDelayMs = postDelayMs;
    }

    public async Task<BatchDispatchResult> ReconcileAsync(
        string channelId,
        string registryPath,
        string journalPath,
        EditorialInput input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            throw new ArgumentException("Channel ID cannot be null or empty.", nameof(channelId));
        if (input == null)
            throw new ArgumentNullException(nameof(input));

        var dispatchRecords = new List<DispatchResultRecord>();
        int totalProcessed = 0;
        int totalSuccessful = 0;

        string todayIso = KyivClock.TodayIso;
        string editionDate = input.EditionDate;
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(editionDate);

        // Guard: Day boundary rule (Plan v6 Section 2: T for date D is only published when Kyiv date >= D)
        if (string.CompareOrdinal(editionDate, todayIso) > 0)
        {
            string msg = $"[INFO][WhatsAppReconciler] Feed EditionDate ({editionDate}) is ahead of Kyiv date ({todayIso}). Holding T publication until midnight Kyiv time.";
            LogSystem(msg);
            return new BatchDispatchResult(true, 0, 0, null, dispatchRecords);
        }

        // 1. Load local journal and legacy registry
        var journal = await _journalStore.LoadAsync(journalPath, cancellationToken).ConfigureAwait(false);
        RegistryModel? legacyRegistry = null;
        if (!string.IsNullOrWhiteSpace(registryPath) && File.Exists(registryPath))
        {
            try
            {
                legacyRegistry = await _registryStore.LoadAsync(registryPath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception regEx)
            {
                LogSystem($"[WARN][WhatsAppReconciler] Could not load whatsapp_registry.json: {regEx.Message}");
            }
        }

        // 2. Read actual tail state from WhatsApp Bridge (`GET /tail-state`)
        var tailState = await _adapter.GetTailStateAsync(channelId, todayDdMm, cancellationToken).ConfigureAwait(false);
        if (!tailState.IsSuccess)
        {
            // Fail-Closed: if feed state cannot be read, perform zero destructive actions and log error
            string err = $"[ERROR][WhatsAppReconciler] Fail-Closed: could not read /tail-state from bridge ({tailState.ErrorDescription}). Zero actions executed.";
            LogSystem(err);
            return new BatchDispatchResult(false, 0, 0, err, dispatchRecords);
        }

        var actualMessages = EnrichActualMessagesFromJournal(tailState.Messages, journal);
        LogSystem($"[INFO][WhatsAppReconciler] Read {actualMessages.Count} message(s) from channel tail ({actualMessages.Count(m => !string.IsNullOrEmpty(m.Marker))} marked).");

        // 3. One-time transition cleanup of today's legacy unmarked posts using IDs from whatsapp_registry.json
        bool hasMarkedTodayInChannel = actualMessages.Any(m => m.MarkerType == "T" && m.MarkerDate == todayDdMm);
        bool legacyCleanupAlreadyDone = string.Equals(journal.LegacyCleanupCompletedDate, editionDate, StringComparison.OrdinalIgnoreCase);

        if (!hasMarkedTodayInChannel && !legacyCleanupAlreadyDone && legacyRegistry != null &&
            string.Equals(legacyRegistry.EditionDate, editionDate, StringComparison.OrdinalIgnoreCase))
        {
            var legacyActiveRecords = legacyRegistry.Publications
                .Where(p => p.TransmissionState != "DELETED" &&
                            !string.IsNullOrWhiteSpace(p.ExternalMessageId) &&
                            !p.ExternalMessageId.StartsWith("wa_virtual_", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (legacyActiveRecords.Count > 0)
            {
                LogSystem($"[INFO][WhatsAppReconciler] Executing one-time transition cleanup of {legacyActiveRecords.Count} unmarked post(s) for {editionDate} from whatsapp_registry.json...");
                foreach (var oldRec in legacyActiveRecords)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalProcessed++;
                    var delRes = await _adapter.DeleteMessageAsync(channelId, oldRec.ExternalMessageId!, cancellationToken).ConfigureAwait(false);
                    if (delRes.IsSuccess) totalSuccessful++;
                    dispatchRecords.Add(new DispatchResultRecord(
                        oldRec.PublisherArtifactId,
                        oldRec.TerritoryId,
                        DecisionResult.Delete.ToString(),
                        delRes.IsSuccess,
                        null,
                        delRes.ErrorDescription,
                        oldRec.PublicationType,
                        oldRec.ExternalMessageId
                    ));
                    await _delayProvider.DelayAsync(800, cancellationToken).ConfigureAwait(false);
                }

                journal = journal with { LegacyCleanupCompletedDate = editionDate };
                await _journalStore.SaveWithRotationAsync(journalPath, journal, 30, cancellationToken).ConfigureAwait(false);

                // Re-read tail state after one-time transition cleanup
                var refreshedTail = await _adapter.GetTailStateAsync(channelId, todayDdMm, cancellationToken).ConfigureAwait(false);
                if (refreshedTail.IsSuccess)
                {
                    actualMessages = EnrichActualMessagesFromJournal(refreshedTail.Messages, journal);
                }
            }
        }

        // 4. Step 1 of Reconciliation: Delete any obsolete F (forecast) with date <= today BEFORE any T actions
        var obsoleteForecasts = actualMessages
            .Where(m => m.MarkerType == "F" && IsMarkerDateLessOrEqualToday(m.MarkerDate, editionDate))
            .ToList();

        foreach (var obsF in obsoleteForecasts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogSystem($"[INFO][WhatsAppReconciler] Deleting obsolete forecast '{obsF.Marker}' (id={obsF.MessageId})...");
            totalProcessed++;
            var delRes = await DeletePostAsync(channelId, obsF.MessageId, obsF.Marker, cancellationToken).ConfigureAwait(false);
            if (delRes.IsSuccess) totalSuccessful++;

            UpsertJournalEntry(journal, obsF.Marker ?? "F", "F", editionDate, obsF.MarkerSeq, "obsolete_forecast", obsF.MessageId, "deleted-hash", "DELETED");
            dispatchRecords.Add(new DispatchResultRecord(Guid.NewGuid(), "tomorrow_cleanup", DecisionResult.Delete.ToString(), delRes.IsSuccess, null, delRes.ErrorDescription, "Text", obsF.MessageId));

            actualMessages.Remove(obsF);
            await _delayProvider.DelayAsync(800, cancellationToken).ConfigureAwait(false);
        }

        // 5. Step 2 of Reconciliation: Remove duplicate markers in channel (keep first for T/F/G; for S keep only latest if multiple)
        var duplicateDeletes = FindDuplicateMarkerMessages(actualMessages, todayDdMm);
        foreach (var dup in duplicateDeletes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogSystem($"[INFO][WhatsAppReconciler] Deleting duplicate marker '{dup.Marker}' (id={dup.MessageId})...");
            totalProcessed++;
            var delRes = await DeletePostAsync(channelId, dup.MessageId, dup.Marker, cancellationToken).ConfigureAwait(false);
            if (delRes.IsSuccess) totalSuccessful++;
            dispatchRecords.Add(new DispatchResultRecord(Guid.NewGuid(), dup.Marker ?? "duplicate", DecisionResult.Delete.ToString(), delRes.IsSuccess, null, delRes.ErrorDescription, "Text", dup.MessageId));

            actualMessages.Remove(dup);
            await _delayProvider.DelayAsync(800, cancellationToken).ConfigureAwait(false);
        }

        // 6. Build DesiredPosts for Today (T), Tomorrow (F), Graphic (G), and Status (S) with stable NN numbering
        var desiredPosts = BuildDesiredPosts(input, journal, actualMessages);

        // 7. Synchronize any marked posts already present in channel into local journal (Self-healing)
        foreach (var actual in actualMessages.Where(m => !string.IsNullOrEmpty(m.Marker)))
        {
            var matchingDesired = desiredPosts.FirstOrDefault(d => string.Equals(d.Marker, actual.Marker, StringComparison.OrdinalIgnoreCase));
            var existingEntry = journal.Entries.FirstOrDefault(e => string.Equals(e.Marker, actual.Marker, StringComparison.OrdinalIgnoreCase));
            if (existingEntry == null && matchingDesired != null)
            {
                string actualCleanHash = _hashCalculator.ComputeHash(WhatsAppContentFormatter.StripMarker(actual.TextWithoutMarker), null);
                UpsertJournalEntry(
                    journal,
                    matchingDesired.Marker,
                    matchingDesired.MarkerType.ToString(),
                    matchingDesired.EditionDate,
                    matchingDesired.SequenceNumber,
                    matchingDesired.TerritoryId,
                    actual.MessageId,
                    actualCleanHash,
                    "SENT"
                );
                LogSystem($"[INFO][WhatsAppReconciler] Adopted existing post '{actual.Marker}' (id={actual.MessageId}) from channel DOM into journal.");
            }
        }

        // 8. Evaluate differences and execute Variant A reconciliation
        var desiredNonStatus = desiredPosts.Where(d => d.MarkerType != 'S').ToList();
        var desiredStatus = desiredPosts.First(d => d.MarkerType == 'S');

        // Active tail messages belonging to today's working set (from T · today · 00 onwards, plus any active F, G, S)
        var workingTail = GetCurrentDayWorkingTail(actualMessages, todayDdMm, journal);

        // Check if any existing F or G in workingTail is no longer desired (e.g. tomorrow forecast cancelled)
        var desiredMarkersSet = desiredPosts.Select(d => d.Marker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var undesiredTailPosts = workingTail
            .Where(m => !string.IsNullOrEmpty(m.Marker) && m.MarkerType != "S" && !desiredMarkersSet.Contains(m.Marker!))
            .ToList();

        foreach (var extra in undesiredTailPosts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogSystem($"[INFO][WhatsAppReconciler] Deleting no-longer-desired post '{extra.Marker}' (id={extra.MessageId})...");
            totalProcessed++;
            var delRes = await DeletePostAsync(channelId, extra.MessageId, extra.Marker, cancellationToken).ConfigureAwait(false);
            if (delRes.IsSuccess) totalSuccessful++;
            UpsertJournalEntry(journal, extra.Marker!, extra.MarkerType ?? "T", editionDate, extra.MarkerSeq, extra.Marker!, null, "deleted-hash", "DELETED");
            dispatchRecords.Add(new DispatchResultRecord(Guid.NewGuid(), extra.Marker, DecisionResult.Delete.ToString(), delRes.IsSuccess, null, delRes.ErrorDescription, "Text", extra.MessageId));
            workingTail.Remove(extra);
            actualMessages.Remove(extra);
            await _delayProvider.DelayAsync(800, cancellationToken).ConfigureAwait(false);
        }

        // Walk through desiredNonStatus (T · 00..NN, F · 00..NN, G) in exact canonical order
        int rebuildFromIndex = -1;
        bool anyMutations = totalProcessed > 0;

        for (int i = 0; i < desiredNonStatus.Count; i++)
        {
            var desired = desiredNonStatus[i];
            var actualAtPos = i < workingTail.Count ? workingTail[i] : null;

            // Case 1: Post at position i does not match the expected marker (missing post, wrong order, or S sitting before new post)
            if (actualAtPos == null || !string.Equals(actualAtPos.Marker, desired.Marker, StringComparison.OrdinalIgnoreCase))
            {
                rebuildFromIndex = i;
                LogSystem($"[INFO][WhatsAppReconciler] Order/presence mismatch at index {i}: expected '{desired.Marker}', found '{actualAtPos?.Marker ?? "NONE"}'. Scheduling tail rebuild from index {i}.");
                break;
            }

            // Case 2: Marker matches at position i -> check if content changed
            bool contentChanged = HasPostContentChanged(desired, actualAtPos, journal);
            if (!contentChanged)
            {
                // No-op: record Keep
                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    desired.TerritoryId,
                    DecisionResult.NoAction.ToString(),
                    true,
                    null,
                    desired.FormattedTextWithMarker,
                    desired.PublicationType.ToString(),
                    actualAtPos.MessageId
                ));
                continue;
            }

            // Content changed for `desired.Marker`!
            // Special protection for T · 00 (journal_header):
            bool isJournalHeader = desired.MarkerType == 'T' && desired.SequenceNumber == 0;
            bool isTransitionFromNoOutagesToActive = isJournalHeader &&
                actualAtPos.TextWithoutMarker.Contains("Планові знеструмлення: відсутні", StringComparison.OrdinalIgnoreCase) &&
                actualAtPos.TextWithoutMarker.Contains("Аварійні знеструмлення: відсутні", StringComparison.OrdinalIgnoreCase) &&
                desiredNonStatus.Count(d => d.MarkerType == 'T') > 1;

            if (isTransitionFromNoOutagesToActive)
            {
                LogSystem($"[INFO][WhatsAppReconciler] T · 00 transitioned from 'No Outages' to active outages. Rebuilding today's tail from index 0.");
                rebuildFromIndex = i;
                break;
            }

            // Attempt in-place edit first
            LogSystem($"[INFO][WhatsAppReconciler] Content changed for '{desired.Marker}' (id={actualAtPos.MessageId}). Attempting in-place edit...");
            totalProcessed++;
            var editRes = await _adapter.UpdateTextMessageAsync(channelId, actualAtPos.MessageId, desired.FormattedTextWithMarker, cancellationToken).ConfigureAwait(false);

            if (editRes.IsSuccess)
            {
                anyMutations = true;
                totalSuccessful++;
                UpsertJournalEntry(journal, desired.Marker, desired.MarkerType.ToString(), desired.EditionDate, desired.SequenceNumber, desired.TerritoryId, actualAtPos.MessageId, desired.ContentHash, "UPDATED");
                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    desired.TerritoryId,
                    DecisionResult.Update.ToString(),
                    true,
                    null,
                    desired.FormattedTextWithMarker,
                    desired.PublicationType.ToString(),
                    actualAtPos.MessageId
                ));
                LogSystem($"[INFO][WhatsAppReconciler] In-place edit succeeded for '{desired.Marker}'.");
            }
            else if (isJournalHeader)
            {
                // Plan v6 Rule: If T · 00 edit window expired, retain morning banner 00 in place to avoid wiping the entire day!
                totalSuccessful++;
                UpsertJournalEntry(journal, desired.Marker, desired.MarkerType.ToString(), desired.EditionDate, desired.SequenceNumber, desired.TerritoryId, actualAtPos.MessageId, desired.ContentHash, "UPDATED");
                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    desired.TerritoryId,
                    DecisionResult.Update.ToString(),
                    true,
                    null,
                    desired.FormattedTextWithMarker,
                    desired.PublicationType.ToString(),
                    actualAtPos.MessageId
                ));
                LogSystem($"[INFO][WhatsAppReconciler] In-place edit for anchor banner '{desired.Marker}' unavailable ({editRes.OperationStatus ?? editRes.ErrorDescription}). Retaining banner at position #0 per v6 protection rule.");
            }
            else
            {
                // Variant A: Edit window expired for territory post T · NN (or F / G) -> rebuild tail from index i!
                LogSystem($"[INFO][WhatsAppReconciler] In-place edit for '{desired.Marker}' failed ({editRes.OperationStatus ?? editRes.ErrorDescription}). Triggering Variant A tail rebuild from index {i}.");
                rebuildFromIndex = i;
                break;
            }
        }

        // 9. Execute Variant A Tail Rebuild if needed
        if (rebuildFromIndex >= 0)
        {
            anyMutations = true;
            var messagesToDelete = workingTail.Skip(rebuildFromIndex).ToList();
            LogSystem($"[INFO][WhatsAppReconciler] Rebuilding tail from index {rebuildFromIndex}: deleting {messagesToDelete.Count} post(s) and publishing {desiredNonStatus.Count - rebuildFromIndex} post(s) + S...");

            foreach (var toDel in messagesToDelete)
            {
                cancellationToken.ThrowIfCancellationRequested();
                totalProcessed++;
                var delRes = await DeletePostAsync(channelId, toDel.MessageId, toDel.Marker, cancellationToken).ConfigureAwait(false);
                if (delRes.IsSuccess) totalSuccessful++;
                await _delayProvider.DelayAsync(800, cancellationToken).ConfigureAwait(false);
            }

            // Publish desiredNonStatus[rebuildFromIndex .. end] one by one with 5-10s pacing
            for (int i = rebuildFromIndex; i < desiredNonStatus.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (i > rebuildFromIndex)
                {
                    await _delayProvider.DelayAsync(_postDelayMs, cancellationToken).ConfigureAwait(false);
                }

                var post = desiredNonStatus[i];
                totalProcessed++;
                var sendRes = await PublishSingleDesiredPostAsync(channelId, post, cancellationToken).ConfigureAwait(false);

                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    post.TerritoryId,
                    DecisionResult.Create.ToString(),
                    sendRes.IsSuccess,
                    null,
                    sendRes.IsSuccess ? post.FormattedTextWithMarker : sendRes.ErrorDescription,
                    post.PublicationType.ToString(),
                    sendRes.MessageId
                ));

                if (!sendRes.IsSuccess)
                {
                    string fatal = $"[ERROR][WhatsAppReconciler] Failed to publish '{post.Marker}' ({post.TerritoryId}): {sendRes.ErrorDescription}";
                    LogSystem(fatal);
                    await _journalStore.SaveWithRotationAsync(journalPath, journal, 30, cancellationToken).ConfigureAwait(false);
                    return new BatchDispatchResult(false, totalProcessed, totalSuccessful, fatal, dispatchRecords);
                }

                totalSuccessful++;
                UpsertJournalEntry(journal, post.Marker, post.MarkerType.ToString(), post.EditionDate, post.SequenceNumber, post.TerritoryId, sendRes.MessageId, post.ContentHash, "SENT");
                LogSystem($"[INFO][WhatsAppReconciler] Published '{post.Marker}' ({post.TerritoryId}) -> ID: {sendRes.MessageId}");
            }

            // Publish fresh S at the very tail
            await _delayProvider.DelayAsync(1500, cancellationToken).ConfigureAwait(false);
            totalProcessed++;
            var statusSendRes = await PublishSingleDesiredPostAsync(channelId, desiredStatus, cancellationToken).ConfigureAwait(false);
            dispatchRecords.Add(new DispatchResultRecord(
                Guid.NewGuid(),
                desiredStatus.TerritoryId,
                DecisionResult.Create.ToString(),
                statusSendRes.IsSuccess,
                null,
                statusSendRes.IsSuccess ? desiredStatus.FormattedTextWithMarker : statusSendRes.ErrorDescription,
                desiredStatus.PublicationType.ToString(),
                statusSendRes.MessageId
            ));

            if (statusSendRes.IsSuccess)
            {
                totalSuccessful++;
                UpsertJournalEntry(journal, desiredStatus.Marker, "S", desiredStatus.EditionDate, null, desiredStatus.TerritoryId, statusSendRes.MessageId, desiredStatus.ContentHash, "SENT");
                LogSystem($"[INFO][WhatsAppReconciler] Published tail status '{desiredStatus.Marker}' -> ID: {statusSendRes.MessageId}");
            }
        }
        else
        {
            // All non-status posts (T, F, G) are in exact order and up to date!
            // Verify S (technical status): must be exactly 1 at the very end of workingTail
            var tailAfterNonStatus = workingTail.Skip(desiredNonStatus.Count).ToList();
            bool statusIsCleanSingleAtTail = tailAfterNonStatus.Count == 1 && tailAfterNonStatus[0].MarkerType == "S";

            if (!statusIsCleanSingleAtTail || anyMutations)
            {
                // Delete any trailing messages after desiredNonStatus and publish 1 clean S at the end
                foreach (var trailing in tailAfterNonStatus)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    totalProcessed++;
                    var delRes = await DeletePostAsync(channelId, trailing.MessageId, trailing.Marker, cancellationToken).ConfigureAwait(false);
                    if (delRes.IsSuccess) totalSuccessful++;
                    await _delayProvider.DelayAsync(600, cancellationToken).ConfigureAwait(false);
                }

                totalProcessed++;
                var statusSendRes = await PublishSingleDesiredPostAsync(channelId, desiredStatus, cancellationToken).ConfigureAwait(false);
                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    desiredStatus.TerritoryId,
                    DecisionResult.Create.ToString(),
                    statusSendRes.IsSuccess,
                    null,
                    statusSendRes.IsSuccess ? desiredStatus.FormattedTextWithMarker : statusSendRes.ErrorDescription,
                    desiredStatus.PublicationType.ToString(),
                    statusSendRes.MessageId
                ));

                if (statusSendRes.IsSuccess)
                {
                    totalSuccessful++;
                    UpsertJournalEntry(journal, desiredStatus.Marker, "S", desiredStatus.EditionDate, null, desiredStatus.TerritoryId, statusSendRes.MessageId, desiredStatus.ContentHash, "SENT");
                    LogSystem($"[INFO][WhatsAppReconciler] Refreshed tail status '{desiredStatus.Marker}' -> ID: {statusSendRes.MessageId}");
                }
            }
            else
            {
                // Complete idempotency: 0 changes in feed, channel order is 100% valid -> 0 actions!
                var existingS = tailAfterNonStatus[0];
                dispatchRecords.Add(new DispatchResultRecord(
                    Guid.NewGuid(),
                    desiredStatus.TerritoryId,
                    DecisionResult.NoAction.ToString(),
                    true,
                    null,
                    desiredStatus.FormattedTextWithMarker,
                    desiredStatus.PublicationType.ToString(),
                    existingS.MessageId
                ));
                LogSystem($"[INFO][WhatsAppReconciler] Channel tail is 100% synchronized and ordered. Zero actions needed (idempotent cycle).");
            }
        }

        // 10. Persist Journal (with 30-day rotation) and synchronized whatsapp_registry.json
        await _journalStore.SaveWithRotationAsync(journalPath, journal, 30, cancellationToken).ConfigureAwait(false);
        await SyncCompatibleRegistryAsync(registryPath, editionDate, desiredPosts, journal, cancellationToken).ConfigureAwait(false);

        return new BatchDispatchResult(true, totalProcessed, totalSuccessful, null, dispatchRecords);
    }

    public List<DesiredWhatsAppPost> BuildDesiredPosts(
        EditorialInput input,
        WhatsAppPostJournalModel journal,
        IReadOnlyList<WhatsAppTailMessage> actualTailMessages)
    {
        var result = new List<DesiredWhatsAppPost>();
        string editionDate = input.EditionDate;

        // 1. Discover existing NN assignments for Today (T) and Tomorrow (F) from both Journal and live DOM (Self-healing)
        string todayDdMm = WhatsAppContentFormatter.FormatMarkerDate(editionDate);
        var todaySeqMap = BuildSequenceMap('T', editionDate, todayDdMm, journal, actualTailMessages);

        // Today packages
        var todayPackages = input.Packages
            .Where(p => !p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) &&
                        !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1a. Today Banner (T · dd.MM · 00)
        var headerPkg = todayPackages.FirstOrDefault(p => p.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase));
        if (headerPkg != null)
        {
            string cleanText = WhatsAppContentFormatter.StripMarker(WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(headerPkg.Content ?? string.Empty));
            string marker = WhatsAppContentFormatter.FormatMarker('T', editionDate, 0);
            string fullText = WhatsAppContentFormatter.AppendMarker(cleanText, marker);
            string hash = _hashCalculator.ComputeHash(cleanText, null);

            result.Add(new DesiredWhatsAppPost(
                marker, 'T', editionDate, 0, "journal_header", cleanText, fullText, hash, headerPkg.GraphicBytes, PublicationType.Text));
        }

        // 1b. Today City & Districts (T · dd.MM · 01..NN)
        var territoryPackages = todayPackages
            .Where(p => !p.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase))
            .ToList();

        AssignSequenceNumbers(territoryPackages, todaySeqMap, isTomorrow: false);

        foreach (var pkg in territoryPackages.OrderBy(p => todaySeqMap[p.TerritoryId]))
        {
            int seq = todaySeqMap[pkg.TerritoryId];
            string cleanText = WhatsAppContentFormatter.StripMarker(WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(pkg.Content ?? string.Empty));
            string marker = WhatsAppContentFormatter.FormatMarker('T', editionDate, seq);
            string fullText = WhatsAppContentFormatter.AppendMarker(cleanText, marker);
            string hash = _hashCalculator.ComputeHash(cleanText, null);

            result.Add(new DesiredWhatsAppPost(
                marker, 'T', editionDate, seq, pkg.TerritoryId, cleanText, fullText, hash, pkg.GraphicBytes, PublicationType.Text));
        }

        // 2. Tomorrow Forecast packages (F · dd.MM · 00..NN)
        var tomorrowPackages = input.Packages
            .Where(p => p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (input.TomorrowForecastAvailable && tomorrowPackages.Count > 0)
        {
            string tomorrowDate = DateTime.TryParse(editionDate, out var dt)
                ? dt.AddDays(1).ToString("yyyy-MM-dd")
                : editionDate;
            string tomDdMm = WhatsAppContentFormatter.FormatMarkerDate(tomorrowDate);
            var tomSeqMap = BuildSequenceMap('F', tomorrowDate, tomDdMm, journal, actualTailMessages);

            var tomBannerPkg = tomorrowPackages.FirstOrDefault(p => p.TerritoryId.Equals("tomorrow_separator", StringComparison.OrdinalIgnoreCase));
            if (tomBannerPkg != null)
            {
                string cleanText = WhatsAppContentFormatter.StripMarker(WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(tomBannerPkg.Content ?? string.Empty));
                if (!cleanText.Contains("ПРОГНОЗ НА ЗАВТРА", StringComparison.OrdinalIgnoreCase))
                {
                    cleanText = $"*ПРОГНОЗ НА ЗАВТРА*\n\n{cleanText}".Trim();
                }
                string marker = WhatsAppContentFormatter.FormatMarker('F', tomorrowDate, 0);
                string fullText = WhatsAppContentFormatter.AppendMarker(cleanText, marker);
                string hash = _hashCalculator.ComputeHash(cleanText, null);

                result.Add(new DesiredWhatsAppPost(
                    marker, 'F', tomorrowDate, 0, "tomorrow_separator", cleanText, fullText, hash, tomBannerPkg.GraphicBytes, PublicationType.Tomorrow));
            }

            var tomTerritoryPkgs = tomorrowPackages
                .Where(p => !p.TerritoryId.Equals("tomorrow_separator", StringComparison.OrdinalIgnoreCase))
                .ToList();

            AssignSequenceNumbers(tomTerritoryPkgs, tomSeqMap, isTomorrow: true);

            foreach (var pkg in tomTerritoryPkgs.OrderBy(p => tomSeqMap[p.TerritoryId]))
            {
                int seq = tomSeqMap[pkg.TerritoryId];
                string cleanText = WhatsAppContentFormatter.StripMarker(WhatsAppContentFormatter.ConvertToWhatsAppMarkdown(pkg.Content ?? string.Empty));
                string marker = WhatsAppContentFormatter.FormatMarker('F', tomorrowDate, seq);
                string fullText = WhatsAppContentFormatter.AppendMarker(cleanText, marker);
                string hash = _hashCalculator.ComputeHash(cleanText, null);

                result.Add(new DesiredWhatsAppPost(
                    marker, 'F', tomorrowDate, seq, pkg.TerritoryId, cleanText, fullText, hash, pkg.GraphicBytes, PublicationType.Tomorrow));
            }
        }

        // 3. Graphic 12-subqueue schedule (G · dd.MM)
        if (input.GraphicPackage != null && input.GraphicPackage.HasAnyOutages)
        {
            string gDate = !string.IsNullOrWhiteSpace(input.GraphicPackage.Metadata.TargetDate)
                ? input.GraphicPackage.Metadata.TargetDate
                : editionDate;
            byte[] svgBytes = _graphicAssembly.AssembleSvg(input.GraphicPackage);
            byte[] pngBytes = _rasterizer.RasterizeSvgToPng(svgBytes, 1080, 1080);

            string cleanCaption = WhatsAppContentFormatter.RenderGraphicScheduleCaption(gDate, input.GraphicPackage.TerritorialScope);
            string gMarker = WhatsAppContentFormatter.FormatMarker('G', gDate);
            string fullCaption = WhatsAppContentFormatter.AppendMarker(cleanCaption, gMarker);
            string gHash = _hashCalculator.ComputeGraphicHash(input.GraphicPackage);

            result.Add(new DesiredWhatsAppPost(
                gMarker, 'G', gDate, null, input.GraphicPackage.TerritorialScope, cleanCaption, fullCaption, gHash, pngBytes, PublicationType.Graphic));
        }

        // 4. Technical Status (S · dd.MM) - Always last
        DateTime syncTime = input.SourceQueryTime ?? KyivClock.Now;
        string cleanStatus = $"Останнє оновлення журналу: *{syncTime:HH:mm}*\nСтан моніторингу: активний";
        string sMarker = WhatsAppContentFormatter.FormatMarker('S', editionDate);
        string fullStatus = WhatsAppContentFormatter.AppendMarker(cleanStatus, sMarker);
        string sHash = _hashCalculator.ComputeHash(cleanStatus, null);

        result.Add(new DesiredWhatsAppPost(
            sMarker, 'S', editionDate, null, "system_status", cleanStatus, fullStatus, sHash, null, PublicationType.Technical));

        return result;
    }

    private static Dictionary<string, int> BuildSequenceMap(
        char markerType,
        string editionDate,
        string dateDdMm,
        WhatsAppPostJournalModel journal,
        IReadOnlyList<WhatsAppTailMessage> actualTailMessages)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // 1. From saved journal (ignore DELETED entries with unresolved/marker-as-territoryId)
        foreach (var entry in journal.Entries.Where(e =>
            !string.Equals(e.State, "DELETED", StringComparison.OrdinalIgnoreCase) &&
            !e.TerritoryId.Contains("·", StringComparison.Ordinal) &&
            string.Equals(e.MarkerType, markerType.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.EditionDate, editionDate, StringComparison.OrdinalIgnoreCase) &&
            e.SequenceNumber.HasValue && e.SequenceNumber.Value >= 1))
        {
            if (!map.ContainsKey(entry.TerritoryId))
            {
                map[entry.TerritoryId] = entry.SequenceNumber!.Value;
            }
        }

        // 2. Self-healing from live channel DOM (if journal was lost or reset)
        bool isTomorrow = markerType == 'F';
        foreach (var msg in actualTailMessages.Where(m =>
            string.Equals(m.MarkerType, markerType.ToString(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(m.MarkerDate, dateDdMm, StringComparison.OrdinalIgnoreCase) &&
            m.MarkerSeq.HasValue && m.MarkerSeq.Value >= 1))
        {
            string? resolvedTid = WhatsAppContentFormatter.ResolveTerritoryIdFromPostText(msg.TextWithoutMarker, isTomorrow);
            if (!string.IsNullOrEmpty(resolvedTid) && !map.ContainsKey(resolvedTid))
            {
                map[resolvedTid] = msg.MarkerSeq!.Value;
            }
        }

        return map;
    }

    private static void AssignSequenceNumbers(
        IReadOnlyList<InputTerritoryPackage> territoryPackages,
        Dictionary<string, int> seqMap,
        bool isTomorrow)
    {
        string cityId = isTomorrow ? "tomorrow_starokostiantyniv" : "starokostiantyniv";

        // City always gets 01 if present and not already assigned
        if (territoryPackages.Any(p => p.TerritoryId.Equals(cityId, StringComparison.OrdinalIgnoreCase)) && !seqMap.ContainsKey(cityId))
        {
            if (!seqMap.Values.Contains(1))
            {
                seqMap[cityId] = 1;
            }
        }

        int nextFree = seqMap.Values.Count > 0 ? seqMap.Values.Max() + 1 : 1;

        foreach (var pkg in territoryPackages)
        {
            if (seqMap.ContainsKey(pkg.TerritoryId)) continue;

            while (seqMap.Values.Contains(nextFree))
            {
                nextFree++;
            }
            seqMap[pkg.TerritoryId] = nextFree;
            nextFree++;
        }
    }

    private bool HasPostContentChanged(
        DesiredWhatsAppPost desired,
        WhatsAppTailMessage actual,
        WhatsAppPostJournalModel journal)
    {
        // 1. For Graphic (G), check hash in journal
        var jEntry = journal.Entries.FirstOrDefault(e => string.Equals(e.Marker, desired.Marker, StringComparison.OrdinalIgnoreCase));
        if (desired.MarkerType == 'G')
        {
            return jEntry == null || !string.Equals(jEntry.ContentHash, desired.ContentHash, StringComparison.OrdinalIgnoreCase);
        }

        // 2. Compare normalized text directly from DOM if available
        if (!string.IsNullOrWhiteSpace(actual.TextWithoutMarker))
        {
            string normActual = NormalizeTextForComparison(WhatsAppContentFormatter.StripMarker(actual.TextWithoutMarker));
            string normDesired = NormalizeTextForComparison(desired.CleanTextWithoutMarker);
            if (string.Equals(normActual, normDesired, StringComparison.Ordinal))
            {
                return false;
            }
        }

        // 3. Also check journal content_hash
        if (jEntry != null && string.Equals(jEntry.ContentHash, desired.ContentHash, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(jEntry.ExternalMessageId, actual.MessageId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeTextForComparison(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.Replace("*", "").Replace("_", "").Replace("`", "").Replace(">", "").Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l));
        return string.Join("\n", lines);
    }

    private static List<WhatsAppTailMessage> EnrichActualMessagesFromJournal(
        IReadOnlyList<WhatsAppTailMessage> rawMessages,
        WhatsAppPostJournalModel journal)
    {
        var enriched = new List<WhatsAppTailMessage>(rawMessages.Count);
        foreach (var m in rawMessages)
        {
            if (!string.IsNullOrEmpty(m.Marker) || string.IsNullOrEmpty(m.MessageId))
            {
                enriched.Add(m);
                continue;
            }

            var matchedJournal = journal.Entries.FirstOrDefault(e =>
                !string.Equals(e.State, "DELETED", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.ExternalMessageId, m.MessageId, StringComparison.OrdinalIgnoreCase));

            if (matchedJournal != null)
            {
                enriched.Add(m with
                {
                    Marker = matchedJournal.Marker,
                    MarkerType = matchedJournal.MarkerType,
                    MarkerDate = WhatsAppContentFormatter.FormatMarkerDate(matchedJournal.EditionDate),
                    MarkerSeq = matchedJournal.SequenceNumber
                });
            }
            else
            {
                enriched.Add(m);
            }
        }
        return enriched;
    }

    private static List<WhatsAppTailMessage> GetCurrentDayWorkingTail(
        List<WhatsAppTailMessage> actualMessages,
        string todayDdMm,
        WhatsAppPostJournalModel journal)
    {
        int startIdx = actualMessages.FindIndex(m =>
            (m.MarkerType == "T" && m.MarkerDate == todayDdMm) ||
            m.MarkerType == "F" ||
            m.MarkerType == "G" ||
            m.MarkerType == "S");

        if (startIdx < 0)
        {
            return new List<WhatsAppTailMessage>();
        }

        return actualMessages.Skip(startIdx)
            .Where(m => !string.IsNullOrEmpty(m.Marker))
            .ToList();
    }

    private static List<WhatsAppTailMessage> FindDuplicateMarkerMessages(
        List<WhatsAppTailMessage> actualMessages,
        string todayDdMm)
    {
        var duplicates = new List<WhatsAppTailMessage>();
        var seenMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // For S, if there are multiple S messages in tail, all except the last one are duplicates
        var statusMessages = actualMessages.Where(m => m.MarkerType == "S").ToList();
        if (statusMessages.Count > 1)
        {
            duplicates.AddRange(statusMessages.Take(statusMessages.Count - 1));
        }

        foreach (var msg in actualMessages)
        {
            if (string.IsNullOrEmpty(msg.Marker) || msg.MarkerType == "S") continue;
            // Only deduplicate within today's and tomorrow's active tail (never touch previous days' history)
            if (msg.MarkerType == "T" && msg.MarkerDate != todayDdMm) continue;

            if (!seenMarkers.Add(msg.Marker))
            {
                duplicates.Add(msg);
            }
        }

        return duplicates;
    }

    private static bool IsMarkerDateLessOrEqualToday(string? markerDateDdMm, string todayIso)
    {
        if (string.IsNullOrWhiteSpace(markerDateDdMm)) return true;
        if (!DateTime.TryParse(todayIso, out var todayDt)) return false;

        var parts = markerDateDdMm.Split('.');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int day) || !int.TryParse(parts[1], out int month))
        {
            return false;
        }

        int year = todayDt.Year;
        if (todayDt.Month == 12 && month == 1) year++;
        else if (todayDt.Month == 1 && month == 12) year--;

        try
        {
            var markerDt = new DateTime(year, month, day);
            return markerDt.Date <= todayDt.Date;
        }
        catch
        {
            return false;
        }
    }

    private async Task<WhatsAppDispatchResult> PublishSingleDesiredPostAsync(
        string channelId,
        DesiredWhatsAppPost post,
        CancellationToken cancellationToken)
    {
        if (post.GraphicBytes != null && post.GraphicBytes.Length > 0)
        {
            return await _adapter.SendMediaMessageAsync(channelId, post.FormattedTextWithMarker, post.GraphicBytes, "image/png", cancellationToken).ConfigureAwait(false);
        }

        return await _adapter.SendTextMessageAsync(channelId, post.FormattedTextWithMarker, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WhatsAppDispatchResult> DeletePostAsync(
        string channelId,
        string? messageId,
        string? marker,
        CancellationToken cancellationToken)
    {
        if (_adapter is WhatsAppBridgeAdapter bridgeAdapter)
        {
            return await bridgeAdapter.DeleteMessageByMarkerAsync(channelId, messageId, marker, cancellationToken).ConfigureAwait(false);
        }

        return await _adapter.DeleteMessageAsync(channelId, messageId ?? string.Empty, cancellationToken).ConfigureAwait(false);
    }

    private static void UpsertJournalEntry(
        WhatsAppPostJournalModel journal,
        string marker,
        string markerType,
        string editionDate,
        int? seqNum,
        string territoryId,
        string? externalMessageId,
        string contentHash,
        string state)
    {
        int existingIdx = journal.Entries.FindIndex(e => string.Equals(e.Marker, marker, StringComparison.OrdinalIgnoreCase));
        int attempts = existingIdx >= 0 ? journal.Entries[existingIdx].Attempts + 1 : 1;

        var entry = new WhatsAppJournalEntry(
            Marker: marker,
            MarkerType: markerType,
            EditionDate: editionDate,
            SequenceNumber: seqNum,
            TerritoryId: territoryId,
            ExternalMessageId: externalMessageId,
            ContentHash: contentHash,
            State: state,
            Attempts: attempts,
            UpdatedAtKyiv: KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss")
        );

        if (existingIdx >= 0)
        {
            journal.Entries[existingIdx] = entry;
        }
        else
        {
            journal.Entries.Add(entry);
        }
    }

    private async Task SyncCompatibleRegistryAsync(
        string registryPath,
        string editionDate,
        IReadOnlyList<DesiredWhatsAppPost> desiredPosts,
        WhatsAppPostJournalModel journal,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(registryPath)) return;

        try
        {
            var pubs = new List<RegistryPublicationRecord>();
            foreach (var d in desiredPosts)
            {
                var jEntry = journal.Entries.FirstOrDefault(e => string.Equals(e.Marker, d.Marker, StringComparison.OrdinalIgnoreCase));
                pubs.Add(new RegistryPublicationRecord(
                    PublisherArtifactId: Guid.NewGuid(),
                    TerritoryId: d.TerritoryId,
                    ExternalMessageId: jEntry?.ExternalMessageId,
                    ContentHash: d.ContentHash,
                    TransmissionState: jEntry?.State ?? "SENT",
                    PublicationType: d.PublicationType == PublicationType.Graphic ? "Graphic" : "Text"
                ));
            }

            var model = new RegistryModel(1, editionDate, "ACTIVE", pubs);
            await _registryStore.SaveAsync(registryPath, model, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogSystem($"[WARN][WhatsAppReconciler] Could not sync compatible registry '{registryPath}': {ex.Message}");
        }
    }

    private static void LogSystem(string message)
    {
        Console.WriteLine(message);
        try
        {
            Directory.CreateDirectory("local/logs/publisher");
            string logLine = $"[{KyivClock.Now:yyyy-MM-dd HH:mm:ss} Kyiv] {message}{Environment.NewLine}";
            File.AppendAllText("local/logs/publisher/sync.log", logLine);
        }
        catch
        {
            // Ignore file lock issues during concurrent logging
        }
    }
}
