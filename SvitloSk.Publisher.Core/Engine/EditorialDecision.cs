using System;

namespace SvitloSk.Publisher.Core.Engine;

public record EditorialDecision(
    DecisionResult DecisionResult,
    PublicationClassification Classification,
    Guid? PublicationId = null,
    string? TerritoryIdentifier = null,
    string? TargetHash = null,
    string? ExternalMessageId = null,
    byte[]? GraphicBytes = null
)
{
    public DecisionResult DecisionResult { get; init; } = DecisionResult;
    public PublicationClassification Classification { get; init; } = Classification;
    public Guid? PublicationId { get; init; } = PublicationId;
    public string? TerritoryIdentifier { get; init; } = TerritoryIdentifier;
    public string? TargetHash { get; init; } = TargetHash;
    public string? ExternalMessageId { get; init; } = ExternalMessageId;
    public byte[]? GraphicBytes { get; init; } = GraphicBytes;

    public EditorialDecision(
        DecisionResult decisionResult,
        PublicationClassification classification,
        Guid? publicationId,
        string? territoryIdentifier,
        string? targetHash,
        int telegramMessageId,
        byte[]? graphicBytes = null
    ) : this(decisionResult, classification, publicationId, territoryIdentifier, targetHash, telegramMessageId.ToString(), graphicBytes)
    {
    }

    public int? TelegramMessageId
    {
        get => int.TryParse(ExternalMessageId, out int id) ? id : null;
        init => ExternalMessageId = value?.ToString();
    }
}
