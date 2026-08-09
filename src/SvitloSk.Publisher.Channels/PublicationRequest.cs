namespace SvitloSk.Publisher.Channels;

public record PublicationRequest(string Id, string? Edition = null, TransportOperation Operation = TransportOperation.CREATE, string? ExternalIdentity = null);
