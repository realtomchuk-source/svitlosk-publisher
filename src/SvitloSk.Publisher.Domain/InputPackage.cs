using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain;

public enum InputPackageType
{
    Text,
    Graphic
}

public enum SourcePortion
{
    Today,
    Tomorrow
}

public record TerritorialPayload(
    string TerritoryId,
    SourcePortion Portion,
    string RawText
);

public record InputPackage(
    Guid PackageId,
    DateTimeOffset GenerationTimestamp,
    string SourceIdentifier,
    string TerritorialScope,
    IReadOnlyCollection<TerritorialPayload> Payloads,
    InputPackageType Type = InputPackageType.Text,
    string? PackageState = null,
    DateOnly? TargetDate = null
);
