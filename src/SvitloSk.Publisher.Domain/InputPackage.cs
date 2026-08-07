using System;

namespace SvitloSk.Publisher.Domain;

public enum InputPackageType
{
    Text,
    Graphic
}

public record InputPackage(
    Guid PackageId,
    DateTimeOffset GenerationTimestamp,
    string SourceIdentifier,
    string TerritorialScope,
    string RawPayload,
    InputPackageType Type = InputPackageType.Text,
    string? PackageState = null,
    DateOnly? TargetDate = null
);
