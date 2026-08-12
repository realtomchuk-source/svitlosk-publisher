using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Application.Model;

public record DispatchResultRecord(
    Guid? PublicationId,
    string? TerritoryIdentifier,
    string DecisionResult,
    bool IsSuccess,
    int? MessageId,
    string? ErrorDescription
);

public record BatchDispatchResult(
    bool IsSuccess,
    int TotalProcessed,
    int TotalSuccessful,
    string? FatalErrorDescription,
    IReadOnlyList<DispatchResultRecord> Results
);
