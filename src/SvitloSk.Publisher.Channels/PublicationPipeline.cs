// Source: DELIVERY_PIPELINE.md
// Section: Orchestrator

using System;

namespace SvitloSk.Publisher.Channels;

public class PublicationPipeline : IPublicationPipeline
{
    private readonly IPublicationPort _port;

    public PublicationPipeline(IPublicationPort port)
    {
        _port = port;
    }

    public void Dispatch(PublicationRequest request)
    {
        throw new NotImplementedException();
    }
}
