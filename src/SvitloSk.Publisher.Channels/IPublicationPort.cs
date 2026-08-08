// Source: PUBLICATION_CHANNEL_INTERFACE.md
// Section: Port

using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Channels;

public interface IPublicationPort
{
    Task<AcceptedPublication> PublishAsync(TransportArtifact artifact, CancellationToken cancellationToken = default);
}
