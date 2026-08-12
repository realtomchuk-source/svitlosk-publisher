using System;

namespace SvitloSk.Publisher.Core.Engine;

public record EditorialDecision(
    DecisionResult DecisionResult,
    PublicationClassification Classification,
    Guid? PublicationId = null,
    string? TerritoryIdentifier = null,
    string? TargetHash = null
)
{
    public DecisionResult DecisionResult { get; init; } = DecisionResult;
    public PublicationClassification Classification { get; init; } = Classification;
    public Guid? PublicationId { get; init; } = PublicationId;
    public string? TerritoryIdentifier { get; init; } = TerritoryIdentifier;
    public string? TargetHash { get; init; } = TargetHash;
}
