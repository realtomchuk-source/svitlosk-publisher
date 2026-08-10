namespace SvitloSk.Publisher.Channels;

public enum TransportArtifactType
{
    TEXT_ONLY,
    SINGLE_MEDIA,
    MEDIA_COLLECTION
}

public enum TransportOperation
{
    CREATE,
    UPDATE,
    DELETE
}

public record TransportArtifact(
    string RequestId,
    TransportArtifactType Type,
    TransportOperation Operation,
    string? Payload = null,
    string? ExternalIdentity = null
);

public record SingleMediaPayload(
    string Media,
    string Caption
);

