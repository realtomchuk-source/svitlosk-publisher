namespace SvitloSk.Publisher.Channels;

public record PublicationRequest(string Id, string? Payload = null, TransportOperation Operation = TransportOperation.CREATE, string? ExternalIdentity = null, TransportArtifactType ArtifactType = TransportArtifactType.TEXT_ONLY);
