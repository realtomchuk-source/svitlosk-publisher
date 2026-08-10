using System;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Channels;

public class PublicationPipeline : IPublicationPipeline
{
    private readonly IPublicationPort _port;

    public PublicationPipeline(IPublicationPort port)
    {
        _port = port;
    }

    public Task<AcceptedPublication> DispatchAsync(PublicationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Operation == TransportOperation.CREATE && string.IsNullOrEmpty(request.Payload))
        {
            throw new NotSupportedException("PublicationRequest payload is empty for CREATE.");
        }
        
        var artifact = new TransportArtifact(request.Id, request.ArtifactType, request.Operation, request.Payload, request.ExternalIdentity, true);
        return _port.PublishAsync(artifact, cancellationToken);
    }
}
