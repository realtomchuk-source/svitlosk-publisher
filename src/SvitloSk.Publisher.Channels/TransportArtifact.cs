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
    string? ExternalIdentity = null,
    bool IsPreformatted = false
);

public record SingleMediaPayload(
    string Media,
    string Caption
);


public class RetryableTransportException : System.Exception
{
    public System.TimeSpan? RetryAfter { get; }
    public RetryableTransportException(string message, System.TimeSpan? retryAfter = null) : base(message)
    {
        RetryAfter = retryAfter;
    }
}
