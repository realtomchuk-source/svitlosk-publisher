using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Application.Model;

public record DispatchResultRecord(
    Guid? PublicationId,
    string? TerritoryIdentifier,
    string DecisionResult,
    bool IsSuccess,
    int? MessageId,
    string? ErrorDescription,
    string PublicationType = "Text",
    string? ExternalMessageId = null
)
{
    public string? ExternalMessageId { get; init; } = ExternalMessageId ?? MessageId?.ToString();
    public int? MessageId => int.TryParse(ExternalMessageId, out int id) ? id : null;

    public DispatchResultRecord(
        Guid? publicationId,
        string? territoryIdentifier,
        string decisionResult,
        bool isSuccess,
        string? externalMessageId,
        string? errorDescription,
        string publicationType = "Text"
    ) : this(publicationId, territoryIdentifier, decisionResult, isSuccess, int.TryParse(externalMessageId, out int id) ? id : null, errorDescription, publicationType, externalMessageId)
    {
    }
}

public record BatchDispatchResult(
    bool IsSuccess,
    int TotalProcessed,
    int TotalSuccessful,
    string? FatalErrorDescription,
    IReadOnlyList<DispatchResultRecord> Results
);
