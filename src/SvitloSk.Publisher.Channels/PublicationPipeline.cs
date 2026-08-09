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
        if (request.Operation == TransportOperation.CREATE && string.IsNullOrEmpty(request.Edition))
        {
            throw new NotSupportedException("PublicationRequest payload is empty for CREATE.");
        }
        
        var type = TransportArtifactType.TEXT_ONLY;
        
        var artifact = new TransportArtifact(request.Id, type, request.Operation, request.Edition, request.ExternalIdentity);
        return _port.PublishAsync(artifact, cancellationToken);
    }
}
