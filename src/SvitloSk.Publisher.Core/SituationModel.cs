using System;
using System.Collections.Generic;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core;

public class SituationModel : ISituationModel
{
    public IReadOnlyCollection<DetectedSituation> Detect(
        Edition currentEdition,
        InputPackage inputPackage,
        DateTimeOffset currentTime,
        TimeSpan cleanupThreshold,
        TimeSpan closeThreshold,
        InfrastructureState infraState)
    {
        var situations = new List<DetectedSituation>();

        // S-01 Morning Startup
        if (currentEdition == null || currentEdition.State == EditionState.Closed)
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
