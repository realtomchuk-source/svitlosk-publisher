using System;
using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core;

public class SituationModel : ISituationModel
{
    private static readonly TimeZoneInfo KyivTz;

    static SituationModel()
    {
        try
        {
            KyivTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
        }
        catch (TimeZoneNotFoundException)
        {
            KyivTz = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        }
    }

    public IReadOnlyCollection<DetectedSituation> Detect(
        Edition? currentEdition,
        InputPackage inputPackage,
        DateTimeOffset currentTime,
        TimeSpan cleanupThreshold,
        TimeSpan closeThreshold,
        InfrastructureState infraState,
        TimeSpan tomorrowEligibilityThreshold = default)
    {
        var situations = new List<DetectedSituation>();

        // S-01 Morning Startup
        if (currentEdition == null || currentEdition.State == EditionState.Closed || currentEdition.State == EditionState.Created)
        {
            situations.Add(new DetectedSituation(Situation.MorningStartup));
        }

        var currentKyivTime = TimeZoneInfo.ConvertTime(currentTime, KyivTz);
        
        // Tomorrow becomes eligible based on threshold
        bool isTomorrowEligible = currentKyivTime.TimeOfDay >= tomorrowEligibilityThreshold;
        
        // Time-based situations (S-11, S-12)
        if (currentEdition != null && currentEdition.State == EditionState.Active)
        {
            var targetTime = currentEdition.TargetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var timeSinceTarget = currentTime - targetTime;

            if (timeSinceTarget >= closeThreshold)
            {
                situations.Add(new DetectedSituation(Situation.EditionClosing));
            }
            else if (currentKyivTime.TimeOfDay >= cleanupThreshold) 
            {
                situations.Add(new DetectedSituation(Situation.CleanupStarted));
            }
        }

        var todayStart = currentKyivTime.Date; // local midnight
        var tomorrowStart = todayStart.AddDays(1);
        var dayAfterStart = todayStart.AddDays(2);

        var todayWindowStart = new DateTimeOffset(todayStart, KyivTz.GetUtcOffset(todayStart));
        var tomorrowWindowStart = new DateTimeOffset(tomorrowStart, KyivTz.GetUtcOffset(tomorrowStart));
        var dayAfterWindowStart = new DateTimeOffset(dayAfterStart, KyivTz.GetUtcOffset(dayAfterStart));

        // Detect package changes
        if (inputPackage != null && inputPackage.Events != null)
        {
            var eventsBySettlement = inputPackage.Events.GroupBy(e => e.Settlement);

            foreach (var group in eventsBySettlement)
            {
                var territoryId = group.Key;
                if (string.IsNullOrEmpty(territoryId)) continue;
                
                var groupEvents = group.ToList();

                var existingTodayPub = currentEdition?.Packages
                    .SelectMany(pkg => pkg.Publications)
                    .FirstOrDefault(p => p.TerritoryId == territoryId && p.Type != PublicationType.Tomorrow);
                    
                var existingTomorrowPub = currentEdition?.Packages
                    .SelectMany(pkg => pkg.Publications)
                    .FirstOrDefault(p => p.TerritoryId == territoryId && p.Type == PublicationType.Tomorrow);

                // Today logic
                var todayHash = EventHashGenerator.GenerateHash(groupEvents, todayWindowStart, tomorrowWindowStart);
                var hasToday = todayHash != EventHashGenerator.GenerateHash(Enumerable.Empty<Event>(), todayWindowStart, tomorrowWindowStart);

                if (existingTodayPub == null)
                {
                    if (hasToday)
                    {
                        situations.Add(new DetectedSituation(Situation.TerritoryAppeared, territoryId));
                    }
                }
                else
                {
                    if (!hasToday)
                    {
                        situations.Add(new DetectedSituation(Situation.TerritoryDisappeared, territoryId));
                    }
                    else if (existingTodayPub.ContentHash != todayHash)
                    {
                        situations.Add(new DetectedSituation(Situation.ChangedAddresses, territoryId));
                    }
                    else
                    {
                        situations.Add(new DetectedSituation(Situation.NoChangesDetected, territoryId));
                    }
                }

                // Tomorrow logic
                if (isTomorrowEligible)
                {
                    var tomorrowHash = EventHashGenerator.GenerateHash(groupEvents, tomorrowWindowStart, dayAfterWindowStart);
                    var hasTomorrow = tomorrowHash != EventHashGenerator.GenerateHash(Enumerable.Empty<Event>(), tomorrowWindowStart, dayAfterWindowStart);

                    if (existingTomorrowPub == null)
                    {
                        if (hasTomorrow)
                        {
                            situations.Add(new DetectedSituation(Situation.TomorrowForecastAppeared, territoryId));
                        }
                    }
                    else
                    {
                        if (!hasTomorrow)
                        {
                            situations.Add(new DetectedSituation(Situation.TomorrowForecastDisappeared, territoryId));
                        }
                        else if (existingTomorrowPub.ContentHash != tomorrowHash)
                        {
                            situations.Add(new DetectedSituation(Situation.TomorrowForecastAppeared, territoryId)); // Spec maps update to Appeared
                        }
                        else
                        {
                            situations.Add(new DetectedSituation(Situation.NoChangesDetected, territoryId));
                        }
                    }
                }
            }
            
            // Check for publications that exist in edition but have no events in the input package
            if (currentEdition != null)
            {
                var pubTerritories = currentEdition.Packages
                    .SelectMany(pkg => pkg.Publications)
                    .Select(p => p.TerritoryId)
                    .Distinct();
                    
                foreach (var tId in pubTerritories)
                {
                    if (!eventsBySettlement.Any(g => g.Key == tId))
                    {
                        var hasTodayPub = currentEdition.Packages.SelectMany(pkg => pkg.Publications).Any(p => p.TerritoryId == tId && p.Type != PublicationType.Tomorrow);
                        var hasTomorrowPub = currentEdition.Packages.SelectMany(pkg => pkg.Publications).Any(p => p.TerritoryId == tId && p.Type == PublicationType.Tomorrow);
                        
                        if (hasTodayPub)
                            situations.Add(new DetectedSituation(Situation.TerritoryDisappeared, tId));
                            
                        if (hasTomorrowPub && isTomorrowEligible)
                            situations.Add(new DetectedSituation(Situation.TomorrowForecastDisappeared, tId));
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
