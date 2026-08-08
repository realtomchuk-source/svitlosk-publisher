using System;

namespace SvitloSk.Publisher.Channels;

public class PublicationPipeline : IPublicationPipeline
{
    private readonly IPublicationPort _port;

    public PublicationPipeline(IPublicationPort port)
    {
        _port = port;
    }

    public AcceptedPublication Dispatch(PublicationRequest request)
    {
        if (string.IsNullOrEmpty(request.Edition))
        {
            throw new NotSupportedException("PublicationRequest payload is empty. Cannot safely infer operation (CREATE/UPDATE/DELETE).");
        }
        
        var op = TransportOperation.CREATE;
        var type = TransportArtifactType.SINGLE_MEDIA;
        
        var artifact = new TransportArtifact(request.Id, type, op, request.Edition);
        return _port.Publish(artifact);
    }
}
