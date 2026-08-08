// Source: DELIVERY_PIPELINE.md
// Section: Orchestrator

using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Channels;

public interface IPublicationPipeline
{
    Task<AcceptedPublication> DispatchAsync(PublicationRequest request, CancellationToken cancellationToken = default);
}
