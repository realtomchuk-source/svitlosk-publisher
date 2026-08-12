using System;

namespace SvitloSk.Publisher.Core.Domain;

public record PublisherArtifact(
    Guid PublisherArtifactId,
    Guid PublicationId,
    string ContentHash
)
{
    public Guid PublisherArtifactId { get; init; } = PublisherArtifactId != Guid.Empty
        ? PublisherArtifactId
        : throw new ArgumentException("PublisherArtifactId cannot be empty.", nameof(PublisherArtifactId));

    public Guid PublicationId { get; init; } = PublicationId != Guid.Empty
        ? PublicationId
        : throw new ArgumentException("PublicationId cannot be empty.", nameof(PublicationId));

    public string ContentHash { get; init; } = !string.IsNullOrWhiteSpace(ContentHash)
        ? ContentHash
        : throw new ArgumentException("ContentHash cannot be empty or whitespace.", nameof(ContentHash));
}
