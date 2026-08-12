using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Domain;

public record Publication(
    Guid PublicationId,
    string TerritoryIdentifier,
    PublicationType PublicationType,
    DateTime CreatedAt,
    string ContentHash,
    PublicationState State = PublicationState.Created,
    bool IsPersistent = true,
    IReadOnlyList<string>? Addresses = null,
    bool? HasTomorrowForecast = null,
    string? ScheduleHash = null
)
{
    public Guid PublicationId { get; init; } = PublicationId != Guid.Empty 
        ? PublicationId 
        : throw new ArgumentException("PublicationId cannot be empty.", nameof(PublicationId));

    public string TerritoryIdentifier { get; init; } = !string.IsNullOrWhiteSpace(TerritoryIdentifier)
        ? TerritoryIdentifier
        : throw new ArgumentException("TerritoryIdentifier cannot be empty or whitespace.", nameof(TerritoryIdentifier));

    public string ContentHash { get; init; } = !string.IsNullOrWhiteSpace(ContentHash)
        ? ContentHash
        : throw new ArgumentException("ContentHash cannot be empty or whitespace.", nameof(ContentHash));

    public PublicationState State { get; init; } = State;
    public bool IsPersistent { get; init; } = IsPersistent;
    public IReadOnlyList<string>? Addresses { get; init; } = Addresses;
    public bool? HasTomorrowForecast { get; init; } = HasTomorrowForecast;
    public string? ScheduleHash { get; init; } = ScheduleHash;
}
