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
        if (string.IsNullOrEmpty(request.Edition))
        {
            throw new NotSupportedException("PublicationRequest payload is empty. Cannot safely infer operation (CREATE/UPDATE/DELETE).");
        }
        
        var op = TransportOperation.CREATE;
        var type = TransportArtifactType.TEXT_ONLY;
        
        var artifact = new TransportArtifact(request.Id, type, op, request.Edition);
        return _port.PublishAsync(artifact, cancellationToken);
    }
}
