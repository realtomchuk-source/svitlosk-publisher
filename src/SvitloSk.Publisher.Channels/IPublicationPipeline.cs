// Source: DELIVERY_PIPELINE.md
// Section: Orchestrator

namespace SvitloSk.Publisher.Channels;

public interface IPublicationPipeline
{
    AcceptedPublication Dispatch(PublicationRequest request);
}
