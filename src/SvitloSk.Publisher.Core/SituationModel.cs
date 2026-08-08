using System;
using System.Collections.Generic;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core;

public class SituationModel : ISituationModel
{
    public IReadOnlyCollection<DetectedSituation> Detect(
        Edition? currentEdition,
        InputPackage inputPackage,
        DateTimeOffset currentTime,
        TimeSpan cleanupThreshold,
        TimeSpan closeThreshold,
        InfrastructureState infraState)
    {
        var situations = new List<DetectedSituation>();

        // S-01 Morning Startup
        if (currentEdition == null || currentEdition.State == EditionState.Closed || currentEdition.State == EditionState.Created)
        {
            situations.Add(new DetectedSituation(Situation.MorningStartup));
        }

        // Time-based situations (S-11, S-12)
        if (currentEdition != null && currentEdition.State == EditionState.Active)
        {
            var targetTime = currentEdition.TargetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var timeSinceTarget = currentTime - targetTime;

            if (timeSinceTarget >= closeThreshold)
            {
                situations.Add(new DetectedSituation(Situation.EditionClosing));
            }
            else if (timeSinceTarget >= cleanupThreshold)
            {
                situations.Add(new DetectedSituation(Situation.CleanupStarted));
            }
        }

        // Detect package changes
        if (inputPackage != null && inputPackage.Payloads != null)
        {
            foreach (var payload in inputPackage.Payloads)
            {
                if (string.IsNullOrEmpty(payload.TerritoryId)) continue;
                
                var existingPub = currentEdition == null ? null : System.Linq.Enumerable.FirstOrDefault(
                    System.Linq.Enumerable.SelectMany(currentEdition.Packages, pkg => pkg.Publications), 
                    p => p.TerritoryId == payload.TerritoryId && 
                         (payload.Portion == SourcePortion.Tomorrow ? p.Type == PublicationType.Tomorrow : p.Type != PublicationType.Tomorrow));
                
                if (existingPub == null)
                {
                    if (payload.RawText != "CLEAR" && !string.IsNullOrWhiteSpace(payload.RawText))
                    {
                        var sit = payload.Portion == SourcePortion.Tomorrow ? Situation.TomorrowForecastAppeared : Situation.TerritoryAppeared;
                        situations.Add(new DetectedSituation(sit, payload.TerritoryId));
                    }
                }
                else
                {
                    if (payload.RawText == "CLEAR" || string.IsNullOrWhiteSpace(payload.RawText))
                    {
                        var sit = payload.Portion == SourcePortion.Tomorrow ? Situation.TomorrowForecastDisappeared : Situation.TerritoryDisappeared;
                        situations.Add(new DetectedSituation(sit, payload.TerritoryId));
                    }
                    else if (existingPub.ContentHash != payload.RawText)
                    {
                        var sit = payload.Portion == SourcePortion.Tomorrow ? Situation.TomorrowForecastAppeared : Situation.ChangedAddresses;
                        situations.Add(new DetectedSituation(sit, payload.TerritoryId));
                    }
                    else
                    {
                        var sit = payload.Portion == SourcePortion.Tomorrow ? Situation.NoChangesDetected : Situation.NoChangesDetected;
                        situations.Add(new DetectedSituation(sit, payload.TerritoryId));
                    }
                }
            }
        }

        // Infrastructure-based situations (S-14, S-15, S-16)
        if (infraState != null)
        {
            if (infraState.IsProducerUnavailable)
            {
                situations.Add(new DetectedSituation(Situation.ExternalProducerUnavailable));
            }

            if (infraState.IsGraphicUnavailable)
            {
                situations.Add(new DetectedSituation(Situation.GraphicUnavailable));
            }

            if (infraState.IsCommentFlood)
            {
                situations.Add(new DetectedSituation(Situation.CommentFlood));
            }
        }

        return situations.AsReadOnly();
    }
}
