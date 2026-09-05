using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Application.Model;

public record RegistryPublicationRecord(
    Guid PublisherArtifactId,
    string TerritoryId,
    int? TelegramMessageId,
    string ContentHash,
    string TransmissionState,
    string PublicationType = "Text"
);

public record RegistryModel(
    int SchemaVersion,
    string EditionDate,
    string Status,
    IReadOnlyList<RegistryPublicationRecord> Publications
);

