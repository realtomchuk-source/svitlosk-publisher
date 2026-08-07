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
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
    }

    [Fact]
    public void Detect_ClosedEdition_ReturnsMorningStartup()
    {
        var edition = new Edition(Guid.NewGuid(), DateOnly.FromDateTime(DateTime.UtcNow));
        edition.Close();
        
        var infraState = new InfrastructureState(false, false, false);
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
    }

    [Fact]
    public void Detect_ActiveEdition_NoTimeTriggers_NoInfraIssues_ReturnsEmpty()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = new Edition(Guid.NewGuid(), targetDate);
        edition.Activate();
        
        var currentTime = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddMinutes(30);
        var infraState = new InfrastructureState(false, false, false);
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), currentTime, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Empty(result);
    }

    [Fact]
    public void Detect_PastCleanupThreshold_ReturnsCleanupStarted()
    {
        var targetDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = new Edition(Guid.NewGuid(), targetDate);
        edition.Activate();
        
        var currentTime = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(1.5);
        var infraState = new InfrastructureState(false, false, false);
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), currentTime, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
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
        
        var result = _model.Detect(edition, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), currentTime, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Contains(result, s => s.Type == Situation.EditionClosing);
    }

    [Fact]
    public void Detect_ProducerDown_ReturnsExternalProducerUnavailable()
    {
        var infraState = new InfrastructureState(true, false, false);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.ExternalProducerUnavailable);
    }

    [Fact]
    public void Detect_GraphicUnavailable_ReturnsGraphicUnavailable()
    {
        var infraState = new InfrastructureState(false, true, false);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.GraphicUnavailable);
    }

    [Fact]
    public void Detect_CommentFlood_ReturnsCommentFlood()
    {
        var infraState = new InfrastructureState(false, false, true);
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        Assert.Contains(result, s => s.Type == Situation.CommentFlood);
    }

    [Fact]
    public void Detect_MultipleTriggers_ReturnsMultipleSituations()
    {
        var infraState = new InfrastructureState(true, false, true); // Producer down, Comment flood
        var result = _model.Detect(null, new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", ""), DateTimeOffset.UtcNow, TimeSpan.FromHours(1), TimeSpan.FromHours(2), infraState);
        
        Assert.Contains(result, s => s.Type == Situation.MorningStartup);
        Assert.Contains(result, s => s.Type == Situation.ExternalProducerUnavailable);
        Assert.Contains(result, s => s.Type == Situation.CommentFlood);
        Assert.Equal(3, result.Count);
    }
}
