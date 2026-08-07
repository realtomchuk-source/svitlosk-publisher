// Source: PUBLICATION_CHANNEL_INTERFACE.md
// Section: Port

namespace SvitloSk.Publisher.Channels;

public interface IPublicationPort
{
    AcceptedPublication Publish(PublicationRequest request);
}
