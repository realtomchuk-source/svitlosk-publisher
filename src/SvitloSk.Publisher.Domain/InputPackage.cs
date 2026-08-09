using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain;

public enum InputPackageType
{
    Text,
    Graphic
}

public record Interval(DateTimeOffset StartTime, DateTimeOffset EndTime);

public record Event(
    string Settlement,
    IReadOnlyCollection<string> Streets,
    IReadOnlyCollection<Interval> Intervals
);

public record InputPackage(
    Guid PackageId,
    DateTimeOffset GenerationTimestamp,
    string SourceIdentifier,
    string TerritorialScope,
    IReadOnlyCollection<Event> Events,
    InputPackageType Type = InputPackageType.Text,
    string? PackageState = null,
    DateOnly? TargetDate = null
);
