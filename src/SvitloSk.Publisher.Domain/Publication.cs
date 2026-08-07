using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain;

public enum PublicationClassification
{
    Persistent,
    Ephemeral
}

public enum PublicationType
{
    Text,
    Graphic,
    Technical,
    Tomorrow
}

public record Publication(
    Guid Id, 
    string TerritoryId, 
    string Content, 
    PublicationClassification Classification,
    PublicationType Type,
    DateTimeOffset CreatedAt,
    string ContentHash,
    IReadOnlyList<string>? Addresses = null,
    bool? HasTomorrowForecast = null,
    string? ScheduleHash = null);
