using System;
using System.Linq;
using Xunit;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Tests;

public class SituationModelTests
{
    private readonly SituationModel _model = new SituationModel();

    [Fact]
    public void Detect_NullEdition_ReturnsMorningStartup()
    {
        var infraState = new InfrastructureState(false, false, false);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
    }

    [Fact]
    public void Detect_ClosedEdition_ReturnsMorningStartup()
    {
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        edition.Close();
        
        var infraState = new InfrastructureState(false, false, false);
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
    }

    [Fact]
    public void Detect_ActiveEdition_NoTimeTriggers_NoInfraIssues_ReturnsNoChangesDetected()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentTime = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMinutes(30);
        
        var kyivTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv") ?? TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        var currentKyiv = TimeZoneInfo.ConvertTime(currentTime, kyivTz);
        var todayStart = currentKyiv.Date;
        var todayWindowStart = new DateTimeOffset(todayStart, kyivTz.GetUtcOffset(todayStart));
        var tomorrowWindowStart = new DateTimeOffset(todayStart.AddDays(1), kyivTz.GetUtcOffset(todayStart.AddDays(1)));
        
        var interval = new Interval(currentTime, currentTime.AddHours(2));
        var ev = new Event("T1", Array.Empty<string>(), new[] { interval });
        var expectedHash = EventHashGenerator.GenerateHash(new[] { ev }, todayWindowStart, tomorrowWindowStart);

        var edition = new Edition(Guid.NewGuid(), targetDate);
        edition.Activate();
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        pkg.AddPublication(new Publication(Guid.NewGuid(), "T1", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, expectedHash));
        
        var infraState = new InfrastructureState(false, false, false);
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), currentTime, "src", "T1", new[] { ev }), currentTime, TimeSpan.FromHours(23), TimeSpan.FromHours(24), infraState);
        
        Assert.Single(result);
        Assert.Equal(Situation.NoChangesDetected, result.First().Type);
    }

    [Fact]
    public void Detect_PastCleanupThreshold_ReturnsCleanupStarted()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = new Edition(Guid.NewGuid(), targetDate);
        edition.Activate();
        
        var currentTime = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(1.5);
        var infraState = new InfrastructureState(false, false, false);
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), currentTime, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Contains(result, s => s.Type == Situation.CleanupStarted);
        Assert.DoesNotContain(result, s => s.Type == Situation.EditionClosing);
    }

    [Fact]
    public void Detect_PastCloseThreshold_ReturnsEditionClosing()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = new Edition(Guid.NewGuid(), targetDate);
        edition.Activate();
        
        var currentTime = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(2.5);
        var infraState = new InfrastructureState(false, false, false);
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), currentTime, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Contains(result, s => s.Type == Situation.EditionClosing);
    }

    [Fact]
    public void Detect_ProducerDown_ReturnsExternalProducerUnavailable()
    {
        var infraState = new InfrastructureState(true, false, false);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.ExternalProducerUnavailable);
    }

    [Fact]
    public void Detect_GraphicUnavailable_ReturnsGraphicUnavailable()
    {
        var infraState = new InfrastructureState(false, true, false);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.GraphicUnavailable);
    }

    [Fact]
    public void Detect_CommentFlood_ReturnsCommentFlood()
    {
        var infraState = new InfrastructureState(false, false, true);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", new[] { new Event("T1", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.CommentFlood);
    }

    [Fact]
    public void Detect_MultipleTriggers_ReturnsMultipleSituations()
    {
        var infraState = new InfrastructureState(true, false, true); // Producer down, Comment flood
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", Array.Empty<Event>()), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
        Assert.Contains(result, s => s.Type == Situation.ExternalProducerUnavailable);
        Assert.Contains(result, s => s.Type == Situation.CommentFlood);
        Assert.Equal(3, result.Count);
    }
}

