using System;
using System.Linq;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit;

public class DecisionEngineTests
{
    private readonly EditorialDecisionEngine _engine = new();

    [Fact]
    public void D01_EditionOpening_ShouldReturnOpen_WhenEditionIsNull()
    {
        var decision = _engine.EvaluateEditionOpening(null);
        Assert.Equal(DecisionResult.Open, decision.DecisionResult);
    }

    [Fact]
    public void D02_PublicationValidity_ShouldReturnNotValid_WhenNoExistingPublication()
    {
        var decision = _engine.EvaluatePublicationValidity("starokostiantyniv", "new-hash", null);
        Assert.Equal(DecisionResult.NotValid, decision.DecisionResult);
        Assert.Equal("starokostiantyniv", decision.TerritoryIdentifier);
        Assert.Equal("new-hash", decision.TargetHash);
    }

    [Fact]
    public void D02_PublicationValidity_ShouldReturnChanged_WhenHashMismatch()
    {
        var existing = new Publication(Guid.NewGuid(), "starokostiantyniv", PublicationType.Text, DateTime.UtcNow, "old-hash");
        var decision = _engine.EvaluatePublicationValidity("starokostiantyniv", "new-hash", existing);
        
        Assert.Equal(DecisionResult.Changed, decision.DecisionResult);
        Assert.Equal(existing.PublicationId, decision.PublicationId);
    }

    [Fact]
    public void D03_PublicationCreation_ShouldReturnCreate_OnNotValid()
    {
        var validity = new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Persistent, TerritoryIdentifier: "staro", TargetHash: "h1");
        var decision = _engine.EvaluatePublicationCreation(validity, PublicationClassification.Persistent);

        Assert.Equal(DecisionResult.Create, decision.DecisionResult);
        Assert.Equal(PublicationClassification.Persistent, decision.Classification);
        Assert.NotNull(decision.PublicationId);
    }

    [Fact]
    public void D04_PublicationUpdate_ShouldReturnUpdate_OnChanged()
    {
        var pubId = Guid.NewGuid();
        var validity = new EditorialDecision(DecisionResult.Changed, PublicationClassification.Persistent, pubId, "staro", "h1");
        var decision = _engine.EvaluatePublicationUpdate(validity);

        Assert.Equal(DecisionResult.Update, decision.DecisionResult);
        Assert.Equal(pubId, decision.PublicationId);
    }

    [Fact]
    public void D05_PublicationRemoval_ShouldDelete_IfEphemeral()
    {
        var pubId = Guid.NewGuid();
        var validity = new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Ephemeral, pubId, "staro");
        var decision = _engine.EvaluatePublicationRemoval(validity);

        Assert.Equal(DecisionResult.Delete, decision.DecisionResult);
    }

    [Fact]
    public void D05_PublicationRemoval_ShouldKeep_IfPersistent()
    {
        var pubId = Guid.NewGuid();
        var validity = new EditorialDecision(DecisionResult.NotValid, PublicationClassification.Persistent, pubId, "staro");
        var decision = _engine.EvaluatePublicationRemoval(validity);

        Assert.Equal(DecisionResult.Keep, decision.DecisionResult);
    }

    [Fact]
    public void D06_TomorrowVisibility_ShouldPromote_IfAvailable()
    {
        var decision = _engine.EvaluateTomorrowVisibility(true);
        Assert.Equal(DecisionResult.Promote, decision.DecisionResult);
    }

    [Fact]
    public void D07_GraphicPublication_ShouldGenerate_IfNew()
    {
        var decision = _engine.EvaluateGraphicGeneration("ghash", null);
        Assert.Equal(DecisionResult.Generate, decision.DecisionResult);
    }

    [Fact]
    public void D08_CommentModeration_ShouldRemove_IfViolating()
    {
        var pubId = Guid.NewGuid();
        var decision = _engine.EvaluateCommentModeration(true, pubId);
        Assert.Equal(DecisionResult.Remove, decision.DecisionResult);
    }

    [Fact]
    public void D09_CleanupExecution_ShouldReturnDeletes_ForActiveEphemeral()
    {
        var edition = new Edition("2026-08-12", EditionState.Active);
        var pub = new Publication(Guid.NewGuid(), "staro", PublicationType.Tomorrow, DateTime.UtcNow, "h1", PublicationState.Published, IsPersistent: false);
        edition.AddPublication(pub);

        var cleanups = _engine.EvaluateCleanupExecution(edition).ToList();
        Assert.Single(cleanups);
        Assert.Equal(DecisionResult.Delete, cleanups[0].DecisionResult);
    }

    [Fact]
    public void D10_EditionClosing_ShouldClose_IfNoActiveEphemeral()
    {
        var edition = new Edition("2026-08-12", EditionState.Active);
        var decision = _engine.EvaluateEditionClosing(edition);
        Assert.Equal(DecisionResult.Close, decision.DecisionResult);
    }

    [Fact]
    public void D10_EditionClosing_ShouldNoAction_IfEphemeralActive()
    {
        var edition = new Edition("2026-08-12", EditionState.Active);
        var pub = new Publication(Guid.NewGuid(), "staro", PublicationType.Tomorrow, DateTime.UtcNow, "h1", PublicationState.Published, IsPersistent: false);
        edition.AddPublication(pub);

        var decision = _engine.EvaluateEditionClosing(edition);
        Assert.Equal(DecisionResult.NoAction, decision.DecisionResult);
    }
}
