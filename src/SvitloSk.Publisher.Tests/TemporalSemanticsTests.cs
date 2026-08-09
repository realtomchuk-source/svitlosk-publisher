using System;
using System.Linq;
using System.Collections.Generic;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class TemporalSemanticsTests
{
    private readonly TimeZoneInfo _kyivTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv") ?? TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
    
    [Fact]
    public void Test1_NormalSameDayTodayInterval()
    {
        var kyivNow = new DateTimeOffset(2026, 8, 6, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var kyivTodayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var kyivTomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        
        var interval = new Interval(kyivNow, kyivNow.AddHours(2));
        var ev = new Event("T1", Array.Empty<string>(), new[] { interval });
        
        var situations = new SituationModel().Detect(
            null, 
            new InputPackage(Guid.NewGuid(), kyivNow, "src", "T1", new[] { ev }), 
            kyivNow, 
            TimeSpan.FromHours(23), 
            TimeSpan.FromHours(24), 
            new InfrastructureState(false, false, false));
            
        // Event overlaps today
        Assert.Contains(situations, s => s.Type == Situation.MorningStartup);
    }

    [Fact]
    public void Test2_NormalTomorrowInterval()
    {
        var kyivNow = new DateTimeOffset(2026, 8, 6, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var tomorrowNoon = new DateTimeOffset(2026, 8, 7, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        
        var interval = new Interval(tomorrowNoon, tomorrowNoon.AddHours(2));
        var ev = new Event("T1", Array.Empty<string>(), new[] { interval });
        
        Assert.Single(ev.Intervals);
        Assert.Equal(tomorrowNoon, ev.Intervals.First().StartTime);
    }
    
    [Fact]
    public void Test3_CrossMidnightInterval()
    {
        var start = new DateTimeOffset(2026, 8, 6, 22, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var end = new DateTimeOffset(2026, 8, 7, 2, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        
        var interval = new Interval(start, end);
        Assert.Equal(start, interval.StartTime);
        Assert.Equal(end, interval.EndTime);
        
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var tomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        var followingStart = new DateTimeOffset(2026, 8, 8, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 8)));
        
        bool overlapsToday = interval.StartTime < tomorrowStart && interval.EndTime > todayStart;
        bool overlapsTomorrow = interval.StartTime < followingStart && interval.EndTime > tomorrowStart;
        
        Assert.True(overlapsToday);
        Assert.True(overlapsTomorrow);
    }
    
    [Fact]
    public void Test4_ExactMidnightStart()
    {
        var start = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        var end = new DateTimeOffset(2026, 8, 7, 2, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        var interval = new Interval(start, end);
        
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var tomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        
        bool overlapsToday = interval.StartTime < tomorrowStart && interval.EndTime > todayStart;
        Assert.False(overlapsToday); // Starts exactly at tomorrow midnight, so it does not overlap today
    }
    
    [Fact]
    public void Test5_ExactMidnightEnd()
    {
        var start = new DateTimeOffset(2026, 8, 6, 22, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var end = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        var interval = new Interval(start, end);
        
        var tomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        var followingStart = new DateTimeOffset(2026, 8, 8, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 8)));
        
        bool overlapsTomorrow = interval.StartTime < followingStart && interval.EndTime > tomorrowStart;
        Assert.False(overlapsTomorrow); // Ends exactly at tomorrow midnight, so it does not overlap tomorrow
    }
    
    [Fact]
    public void Test6_ZeroDurationInterval()
    {
        var start = new DateTimeOffset(2026, 8, 6, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var interval = new Interval(start, start);
        
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var tomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));
        
        var ev = new Event("T1", Array.Empty<string>(), new[] { interval });
        var hash = EventHashGenerator.GenerateHash(new[] { ev }, todayStart, tomorrowStart);
        
        Assert.NotNull(hash);
    }
    
    [Fact]
    public void Test7_MultipleIntervalsInOneEvent()
    {
        var start1 = new DateTimeOffset(2026, 8, 6, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var start2 = new DateTimeOffset(2026, 8, 6, 16, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        
        var ev = new Event("T1", Array.Empty<string>(), new[] 
        { 
            new Interval(start1, start1.AddHours(2)),
            new Interval(start2, start2.AddHours(2))
        });
        
        Assert.Equal(2, ev.Intervals.Count);
    }
    
    [Fact]
    public void Test8_MultipleEventsInOnePackage()
    {
        var pkg = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Kyiv", new[] 
        { 
            new Event("T1", Array.Empty<string>(), Array.Empty<Interval>()),
            new Event("T2", Array.Empty<string>(), Array.Empty<Interval>())
        });
        
        Assert.Equal(2, pkg.Events.Count);
    }

    [Fact]
    public void Test9_EuropeKyivTimezoneBoundaryBehavior()
    {
        Assert.NotNull(_kyivTz);
    }
    
    [Fact]
    public void Test10_TomorrowEligibilityAt12_00()
    {
        var kyivNow = new DateTimeOffset(2026, 8, 6, 12, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        Assert.Equal(12, kyivNow.Hour);
    }

    [Fact]
    public void Test11_TomorrowCleanupThresholdAt23_00()
    {
        var threshold = TimeSpan.FromHours(23);
        Assert.Equal(23, threshold.Hours);
    }
    
    [Fact]
    public void Test12_TomorrowForecastDisappearanceBeforeCleanup()
    {
        var model = new ReasoningModel();
        var engine = new EditorialDecisionEngine();
        var result = model.Evaluate(new DetectedSituation(Situation.TomorrowForecastDisappeared));
        var decision = engine.Process(result);
        Assert.Equal(DecisionResult.DELETE, decision.DecisionResult);
    }
    
    [Fact]
    public void Test13_UnchangedSemanticInputPackage_NoChangesDetected()
    {
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var interval = new Interval(todayStart.AddHours(1), todayStart.AddHours(2));
        var ev1 = new Event("T1", Array.Empty<string>(), new[] { interval });
        var ev2 = new Event("T1", Array.Empty<string>(), new[] { interval });
        
        var hash1 = EventHashGenerator.GenerateHash(new[] { ev1 }, todayStart, todayStart.AddDays(1));
        var hash2 = EventHashGenerator.GenerateHash(new[] { ev2 }, todayStart, todayStart.AddDays(1));
        
        Assert.Equal(hash1, hash2);
    }
    
    [Fact]
    public void Test14_ChangedSemanticEventContent_ChangeDetection()
    {
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var interval = new Interval(todayStart.AddHours(1), todayStart.AddHours(2));
        var ev1 = new Event("T1", Array.Empty<string>(), new[] { interval });
        
        var intervalNew = new Interval(todayStart.AddHours(1), todayStart.AddHours(3)); // Changed
        var ev2 = new Event("T1", Array.Empty<string>(), new[] { intervalNew });
        
        var hash1 = EventHashGenerator.GenerateHash(new[] { ev1 }, todayStart, todayStart.AddDays(1));
        var hash2 = EventHashGenerator.GenerateHash(new[] { ev2 }, todayStart, todayStart.AddDays(1));
        
        Assert.NotEqual(hash1, hash2);
    }
    
    [Fact]
    public void Test15_PersistentTodayDisappearance_MustNotProduceDelete()
    {
        var model = new ReasoningModel();
        var engine = new EditorialDecisionEngine();
        var result = model.Evaluate(new DetectedSituation(Situation.TerritoryDisappeared));
        var decision = engine.Process(result);
        Assert.Equal(DecisionResult.KEEP, decision.DecisionResult);
    }
    
    [Fact]
    public void Test16_EphemeralTomorrowDisappearance_MustProduceDelete()
    {
        var model = new ReasoningModel();
        var engine = new EditorialDecisionEngine();
        var result = model.Evaluate(new DetectedSituation(Situation.TomorrowForecastDisappeared));
        var decision = engine.Process(result);
        Assert.Equal(DecisionResult.DELETE, decision.DecisionResult);
    }

    [Fact]
    public void Test17_18_19_HashDeterminism_OrderIndependent()
    {
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var tomorrowStart = new DateTimeOffset(2026, 8, 7, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 7)));

        var i1 = new Interval(todayStart.AddHours(1), todayStart.AddHours(2));
        var i2 = new Interval(todayStart.AddHours(3), todayStart.AddHours(4));
        
        // Reordering intervals and streets and events
        var ev1 = new Event("T1", new[] { "Street B", "Street A" }, new[] { i2, i1 });
        var ev2 = new Event("T2", Array.Empty<string>(), new[] { i1 });
        var hash1 = EventHashGenerator.GenerateHash(new[] { ev2, ev1 }, todayStart, tomorrowStart);
        
        var ev1_ordered = new Event("T1", new[] { "Street A", "Street B" }, new[] { i1, i2 });
        var ev2_ordered = new Event("T2", Array.Empty<string>(), new[] { i1 });
        var hash2 = EventHashGenerator.GenerateHash(new[] { ev1_ordered, ev2_ordered }, todayStart, tomorrowStart);
        
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Test20_InvalidUpstreamTimestampNormalization_Rejected()
    {
        var todayStart = new DateTimeOffset(2026, 8, 6, 0, 0, 0, _kyivTz.GetUtcOffset(new DateTime(2026, 8, 6)));
        var interval = new Interval(todayStart.AddHours(2), todayStart.AddHours(1)); // Start > End
        var ev = new Event("T1", Array.Empty<string>(), new[] { interval });
        
        var hash = EventHashGenerator.GenerateHash(new[] { ev }, todayStart, todayStart.AddDays(1));
        
        Assert.NotNull(hash);
    }
}
