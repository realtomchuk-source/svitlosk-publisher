using System;
using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Core.Domain;

namespace SvitloSk.Publisher.Core.Engine;

public class EditorialDecisionEngine
{
    // D-01: Edition Opening
    public EditorialDecision EvaluateEditionOpening(Edition? todayEdition)
    {
        if (todayEdition == null)
        {
            return new EditorialDecision(DecisionResult.Open, PublicationClassification.Persistent);
        }
        
        return new EditorialDecision(DecisionResult.NoAction, PublicationClassification.Persistent);
    }

    // D-02: Publication Validity
    public EditorialDecision EvaluatePublicationValidity(string territoryId, string incomingHash, Publication? existingPublication)
    {
        if (existingPublication == null || 
            existingPublication.State == PublicationState.Removed ||
            !existingPublication.TerritoryIdentifier.Equals(territoryId, StringComparison.OrdinalIgnoreCase))
        {
            return new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Persistent, TerritoryIdentifier: territoryId, TargetHash: incomingHash);
        }

        if (existingPublication.ContentHash != incomingHash)
        {
            return new EditorialDecision(DecisionResult.Changed, existingPublication.IsPersistent ? PublicationClassification.Persistent : PublicationClassification.Ephemeral, existingPublication.PublicationId, territoryId, incomingHash);
        }

        return new EditorialDecision(DecisionResult.Valid, existingPublication.IsPersistent ? PublicationClassification.Persistent : PublicationClassification.Ephemeral, existingPublication.PublicationId, territoryId, incomingHash);
    }

    // D-03: Publication Creation
    public EditorialDecision EvaluatePublicationCreation(EditorialDecision validityDecision, PublicationClassification classification)
    {
        if (validityDecision.DecisionResult == DecisionResult.NotValid)
        {
            return new EditorialDecision(
                DecisionResult.Create, 
                classification, 
                PublicationId: Guid.NewGuid(), 
                TerritoryIdentifier: validityDecision.TerritoryIdentifier, 
                TargetHash: validityDecision.TargetHash
            );
        }

        return new EditorialDecision(DecisionResult.NoAction, classification);
    }

    // D-04: Publication Update
    public EditorialDecision EvaluatePublicationUpdate(EditorialDecision validityDecision)
    {
        if (validityDecision.DecisionResult == DecisionResult.Changed)
        {
            return new EditorialDecision(
                DecisionResult.Update,
                validityDecision.Classification,
                PublicationId: validityDecision.PublicationId,
                TerritoryIdentifier: validityDecision.TerritoryIdentifier,
                TargetHash: validityDecision.TargetHash
            );
        }

        return new EditorialDecision(DecisionResult.NoAction, validityDecision.Classification);
    }

    // D-05: Publication Removal (Deletion for Ephemeral, Keep for Persistent)
    public EditorialDecision EvaluatePublicationRemoval(EditorialDecision validityDecision)
    {
        if (validityDecision.DecisionResult == DecisionResult.NotValid)
        {
            if (validityDecision.Classification == PublicationClassification.Ephemeral)
            {
                return new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, validityDecision.PublicationId, validityDecision.TerritoryIdentifier);
            }
            else
            {
                return new EditorialDecision(DecisionResult.Keep, PublicationClassification.Persistent, validityDecision.PublicationId, validityDecision.TerritoryIdentifier);
            }
        }

        return new EditorialDecision(DecisionResult.NoAction, validityDecision.Classification);
    }

    // D-06: Tomorrow Visibility
    public EditorialDecision EvaluateTomorrowVisibility(bool tomorrowForecastAvailable)
    {
        if (tomorrowForecastAvailable)
        {
            return new EditorialDecision(DecisionResult.Promote, PublicationClassification.Ephemeral);
        }

        return new EditorialDecision(DecisionResult.NoAction, PublicationClassification.Ephemeral);
    }

    // D-07: Graphic Publication
    public EditorialDecision EvaluateGraphicGeneration(string incomingScheduleHash, Publication? existingGraphicPublication)
    {
        string? existingHash = existingGraphicPublication?.ContentHash ?? existingGraphicPublication?.ScheduleHash;
        if (existingGraphicPublication == null || existingHash != incomingScheduleHash)
        {
            return new EditorialDecision(DecisionResult.Generate, PublicationClassification.Persistent, TargetHash: incomingScheduleHash);
        }

        return new EditorialDecision(DecisionResult.NoAction, PublicationClassification.Persistent);
    }


    // D-08: Comment Moderation
    public EditorialDecision EvaluateCommentModeration(bool isViolatingPolicy, Guid publicationId)
    {
        if (isViolatingPolicy)
        {
            return new EditorialDecision(DecisionResult.Remove, PublicationClassification.Persistent, publicationId);
        }

        return new EditorialDecision(DecisionResult.Keep, PublicationClassification.Persistent, publicationId);
    }

    // D-09: Cleanup Execution
    public IEnumerable<EditorialDecision> EvaluateCleanupExecution(Edition edition)
    {
        if (edition.State != EditionState.Active)
            yield break;

        foreach (var pub in edition.Publications.Where(p => !p.IsPersistent && p.State == PublicationState.Published))
        {
            yield return new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, pub.PublicationId, pub.TerritoryIdentifier);
        }
    }

    // D-10: Edition Closing
    public EditorialDecision EvaluateEditionClosing(Edition edition)
    {
        // Must close only if all ephemeral publications are removed
        bool hasActiveEphemeral = edition.Publications.Any(p => !p.IsPersistent && p.State != PublicationState.Removed);
        if (hasActiveEphemeral)
        {
            return new EditorialDecision(DecisionResult.NoAction, PublicationClassification.Persistent);
        }

        return new EditorialDecision(DecisionResult.Close, PublicationClassification.Persistent);
    }
}
