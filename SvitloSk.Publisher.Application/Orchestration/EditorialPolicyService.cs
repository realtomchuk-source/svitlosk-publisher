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
        Publication? existingTechPub)
    {
        var result = new List<EditorialDecision>();
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
        bool isPhysicallyAboveOtherPosts = techMsgId.HasValue && existingRecords != null && existingRecords.Any(p =>
            !p.TerritoryId.Equals("system_status", StringComparison.OrdinalIgnoreCase) &&
            p.TransmissionState != "DELETED" &&
            p.TelegramMessageId.HasValue &&
            p.TelegramMessageId.Value > techMsgId.Value);

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
                    if (techMsgId.HasValue)
                    {
                        result.Add(techUpdate with { TelegramMessageId = techMsgId, ExternalMessageId = techExtId, TargetHash = statusContent });
                    }
                    else if (hasExistingStatus)
                    {
                        // External channels (e.g. WhatsApp): delete previous status and create new one at tail
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
}
