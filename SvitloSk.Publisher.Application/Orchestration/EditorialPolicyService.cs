using System;
using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Application.Orchestration;

/// <summary>
/// Domain policy service enforcing business and sequencing rules per svitlosk-specification.
/// Enforces tail-positioning of system_status, tomorrow forecasts visibility, and date rollovers.
/// </summary>
public class EditorialPolicyService
{
    private readonly EditorialDecisionEngine _decisionEngine;
    private readonly ContentHashCalculator _hashCalculator;

    public EditorialPolicyService(EditorialDecisionEngine decisionEngine, ContentHashCalculator hashCalculator)
    {
        _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        _hashCalculator = hashCalculator ?? throw new ArgumentNullException(nameof(hashCalculator));
    }

    /// <summary>
    /// Evaluates system status positioning (tail invariant).
    /// If new journal publications are created or if the status message is physically above active posts,
    /// it triggers a Delete of the old status and Create of a new one at the bottom.
    /// </summary>
    public IReadOnlyList<EditorialDecision> EvaluateSystemStatus(
        string statusContent,
        IReadOnlyList<EditorialDecision> precedingDecisions,
        IReadOnlyList<Model.RegistryPublicationRecord>? existingRecords,
        Publication? existingTechPub,
        bool isDateRollover = false,
        bool isWhatsApp = false)
    {
        var result = new List<EditorialDecision>();

        if (isDateRollover)
        {
            // On date rollover, yesterday's ephemeral system_status is already scheduled for deletion/archive in Pre-Flight barrier.
            // A fresh system_status must be created at the absolute tail of today's new edition.
            result.Add(new EditorialDecision(
                DecisionResult.Create,
                PublicationClassification.Ephemeral,
                Guid.NewGuid(),
                "system_status",
                statusContent
            ));
            return result;
        }

        string techHash = _hashCalculator.ComputeHash(statusContent, null);

        var techValidity = _decisionEngine.EvaluatePublicationValidity("system_status", techHash, existingTechPub);

        int? techMsgId = null;
        string? techExtId = null;
        Guid? existingTechArtifactId = null;
        if (existingRecords != null)
        {
            var record = existingRecords.FirstOrDefault(p => 
                p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
                p.TransmissionState != "DELETED" &&
                (p.TelegramMessageId.HasValue || !string.IsNullOrEmpty(p.ExternalMessageId)));
            techMsgId = record?.TelegramMessageId;
            techExtId = record?.ExternalMessageId ?? record?.TelegramMessageId?.ToString();
            existingTechArtifactId = record?.PublisherArtifactId;
        }

        bool hasExistingStatus = !string.IsNullOrEmpty(techExtId);
        bool anyNewJournalCreates = precedingDecisions.Any(d => d.DecisionResult == DecisionResult.Create);
        int techIndex = -1;
        int lastOtherIndex = -1;
        if (isWhatsApp && existingRecords != null)
        {
            var recordsList = existingRecords.ToList();
            techIndex = recordsList.FindLastIndex(p => p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) && p.TransmissionState != "DELETED");
            lastOtherIndex = recordsList.FindLastIndex(p => !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) && p.TransmissionState != "DELETED");
        }

        bool isPhysicallyAboveOtherPosts = (techMsgId.HasValue && existingRecords != null && existingRecords.Any(p =>
            !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
            p.TransmissionState != "DELETED" &&
            p.TelegramMessageId.HasValue &&
            p.TelegramMessageId.Value > techMsgId.Value))
            || (isWhatsApp && techIndex >= 0 && lastOtherIndex > techIndex);

        bool shouldRecreateAtTail = (anyNewJournalCreates || isPhysicallyAboveOtherPosts) && hasExistingStatus;

        if (shouldRecreateAtTail)
        {
            // 1. Delete previous system_status from stream
            result.Add(new EditorialDecision(
                DecisionResult.Delete,
                PublicationClassification.Ephemeral,
                existingTechArtifactId ?? existingTechPub?.PublicationId ?? Guid.NewGuid(),
                "system_status",
                null,
                techExtId
            ));

            // 2. Create fresh system_status at the bottom of the stream
            result.Add(new EditorialDecision(
                DecisionResult.Create,
                PublicationClassification.Ephemeral,
                Guid.NewGuid(),
                "system_status",
                statusContent
            ));
        }
        else
        {
            var techCreate = _decisionEngine.EvaluatePublicationCreation(techValidity, PublicationClassification.Ephemeral);
            if (techCreate.DecisionResult == DecisionResult.Create)
            {
                result.Add(techCreate with { TargetHash = statusContent });
            }
            else
            {
                var techUpdate = _decisionEngine.EvaluatePublicationUpdate(techValidity);
                if (techUpdate.DecisionResult == DecisionResult.Update)
                {
                    if (isWhatsApp && hasExistingStatus)
                    {
                        // In WhatsApp channels, in-place edit is not supported by newsletter servers.
                        // When status content/timestamp changes, delete previous status and publish fresh at tail.
                        result.Add(new EditorialDecision(
                            DecisionResult.Delete,
                            PublicationClassification.Ephemeral,
                            existingTechArtifactId ?? existingTechPub?.PublicationId ?? Guid.NewGuid(),
                            "system_status",
                            null,
                            techExtId
                        ));
                        result.Add(new EditorialDecision(
                            DecisionResult.Create,
                            PublicationClassification.Ephemeral,
                            Guid.NewGuid(),
                            "system_status",
                            statusContent
                        ));
                    }
                    else if (techMsgId.HasValue || !string.IsNullOrEmpty(techExtId))
                    {
                        result.Add(techUpdate with { TelegramMessageId = techMsgId, ExternalMessageId = techExtId, TargetHash = statusContent });
                    }
                    else
                    {
                        var techCreateFallback = _decisionEngine.EvaluatePublicationCreation(
                            new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Ephemeral, TerritoryIdentifier: "system_status", TargetHash: techHash),
                            PublicationClassification.Ephemeral);
                        result.Add(techCreateFallback with { TargetHash = statusContent });
                    }
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates tomorrow forecast positioning invariant across all channels (Telegram, WhatsApp, Facebook).
    /// Ensures that Today's publications always precede Tomorrow's forecast publications.
    /// If any new Today publication is created or recreated (or if an active Today publication is already
    /// physically positioned below/after Tomorrow's forecast), existing Tomorrow posts are deleted and
    /// recreated at the bottom of Today's section.
    /// </summary>
    public (IReadOnlyList<EditorialDecision> VisibilityDecisions, IReadOnlyList<EditorialDecision> TomorrowDecisions) EvaluateTomorrowOrdering(
        IReadOnlyList<EditorialDecision> todayDecisions,
        IReadOnlyList<EditorialDecision> currentTomorrowDecisions,
        IReadOnlyList<Model.InputTerritoryPackage> allInputPackages,
        string editionDate,
        IReadOnlyList<Model.RegistryPublicationRecord>? existingRecords,
        bool isDateRollover = false)
    {
        var visibilityDeletes = new List<EditorialDecision>();
        if (isDateRollover || existingRecords == null || existingRecords.Count == 0)
        {
            return (visibilityDeletes, currentTomorrowDecisions);
        }

        var activeTomorrowRecords = existingRecords.Where(p =>
            p.TransmissionState != "DELETED" &&
            (p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) ||
             p.TerritoryId.Equals("fb_tomorrow", StringComparison.OrdinalIgnoreCase)) &&
            (p.TelegramMessageId.HasValue || (!string.IsNullOrEmpty(p.ExternalMessageId) && !p.ExternalMessageId.StartsWith("wa_virtual_"))))
            .ToList();

        if (activeTomorrowRecords.Count == 0)
        {
            return (visibilityDeletes, currentTomorrowDecisions);
        }

        bool anyNewTodayCreates = todayDecisions.Any(d => d.DecisionResult == DecisionResult.Create);

        // Check Facebook banner transition where an Update on fb_planned will recreate the post at the top/bottom
        bool isFacebookTodayBannerRecreated = false;
        var fbPlannedUpdate = todayDecisions.FirstOrDefault(d =>
            d.DecisionResult == DecisionResult.Update &&
            string.Equals(d.TerritoryIdentifier, "fb_planned", StringComparison.OrdinalIgnoreCase));
        if (fbPlannedUpdate != null)
        {
            var existingFbPlanned = existingRecords.FirstOrDefault(p =>
                string.Equals(p.TerritoryId, "fb_planned", StringComparison.OrdinalIgnoreCase) &&
                p.TransmissionState != "DELETED");
            string noOutagesText = "<b>Планові знеструмлення:</b> відсутні\n<b>Аварійні знеструмлення:</b> відсутні";
            bool incomingIsNoOutages = fbPlannedUpdate.TargetHash != null &&
                fbPlannedUpdate.TargetHash.Contains("Планові знеструмлення: відсутні", StringComparison.OrdinalIgnoreCase) &&
                fbPlannedUpdate.TargetHash.Contains("Аварійні знеструмлення: відсутні", StringComparison.OrdinalIgnoreCase);
            if (existingFbPlanned != null && !incomingIsNoOutages)
            {
                string noOutagesHash = _hashCalculator.ComputeHash(noOutagesText, null);
                if (existingFbPlanned.ContentHash == noOutagesHash)
                {
                    isFacebookTodayBannerRecreated = true;
                }
            }
        }

        // Check physical message ordering in Telegram (any active tomorrow message has ID < any active today message ID)
        var activeTodayRecords = existingRecords.Where(p =>
            p.TransmissionState != "DELETED" &&
            p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase) &&
            !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
            !p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) &&
            !p.TerritoryId.Equals("fb_tomorrow", StringComparison.OrdinalIgnoreCase) &&
            (p.TelegramMessageId.HasValue || (!string.IsNullOrEmpty(p.ExternalMessageId) && !p.ExternalMessageId.StartsWith("wa_virtual_"))))
            .ToList();

        bool isTomorrowPhysicallyAboveTodayInTelegram = activeTomorrowRecords.Any(tom =>
            tom.TelegramMessageId.HasValue &&
            activeTodayRecords.Any(tod => tod.TelegramMessageId.HasValue && tod.TelegramMessageId.Value > tom.TelegramMessageId.Value));

        // Check registry / channel sequence ordering (e.g., WhatsApp where newly created today posts were placed before unchanged tomorrow posts while other today posts are after, or vice versa)
        bool isTomorrowOutOfOrderInRegistry = false;
        if (!activeTomorrowRecords.Any(t => t.TelegramMessageId.HasValue))
        {
            var recordsList = existingRecords.Where(p => p.TransmissionState != "DELETED" && p.PublicationType.Equals("Text", StringComparison.OrdinalIgnoreCase)).ToList();
            int firstTomorrowIdx = recordsList.FindIndex(p =>
                p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) ||
                p.TerritoryId.Equals("fb_tomorrow", StringComparison.OrdinalIgnoreCase));
            int lastTodayIdx = recordsList.FindLastIndex(p =>
                !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
                !p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) &&
                !p.TerritoryId.Equals("fb_tomorrow", StringComparison.OrdinalIgnoreCase));

            // Also check if system_status is currently sitting BEFORE tomorrow_separator in the registry (symptom of a mid-day today post creation before this fix)
            int statusIdx = recordsList.FindIndex(p => p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase));

            if (firstTomorrowIdx >= 0 && ((lastTodayIdx > firstTomorrowIdx) || (statusIdx >= 0 && statusIdx < firstTomorrowIdx)))
            {
                isTomorrowOutOfOrderInRegistry = true;
            }
        }

        bool shouldRepositionTomorrow = anyNewTodayCreates ||
                                        isFacebookTodayBannerRecreated ||
                                        isTomorrowPhysicallyAboveTodayInTelegram ||
                                        isTomorrowOutOfOrderInRegistry;

        if (!shouldRepositionTomorrow)
        {
            return (visibilityDeletes, currentTomorrowDecisions);
        }

        // 1. Delete all existing active tomorrow posts so they are removed from above Today's posts
        foreach (var oldTom in activeTomorrowRecords)
        {
            visibilityDeletes.Add(new EditorialDecision(
                DecisionResult.Delete,
                PublicationClassification.Ephemeral,
                oldTom.PublisherArtifactId,
                oldTom.TerritoryId,
                null,
                oldTom.ExternalMessageId ?? oldTom.TelegramMessageId?.ToString()
            ) { TelegramMessageId = oldTom.TelegramMessageId });
        }

        // 2. Build fresh Create decisions for all current tomorrow input packages at the bottom of Today's section
        string tomorrowScheduleDate = editionDate;
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            tomorrowScheduleDate = parsedDate.AddDays(1).ToString("yyyy-MM-dd");
        }

        var tomorrowPackages = allInputPackages.Where(p =>
            p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase) ||
            p.TerritoryId.Equals("fb_tomorrow", StringComparison.OrdinalIgnoreCase)).ToList();

        var recreatedTomorrowDecisions = new List<EditorialDecision>();
        foreach (var tomPkg in tomorrowPackages)
        {
            recreatedTomorrowDecisions.Add(new EditorialDecision(
                DecisionResult.Create,
                PublicationClassification.Ephemeral,
                Guid.NewGuid(),
                tomPkg.TerritoryId,
                tomPkg.Content,
                ExternalMessageId: null,
                GraphicBytes: tomPkg.GraphicBytes,
                Type: PublicationType.Text,
                ScheduleDate: tomorrowScheduleDate
            ));
        }

        return (visibilityDeletes, recreatedTomorrowDecisions);
    }
}
