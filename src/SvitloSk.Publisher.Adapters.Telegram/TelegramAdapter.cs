// Source: TELEGRAM_ADAPTER_ARCHITECTURE.md
// Section: 1

using System;
using SvitloSk.Publisher.Channels;

namespace SvitloSk.Publisher.Adapters.Telegram;

public class TelegramAdapter : IPublicationPort
{
    public AcceptedPublication Publish(PublicationRequest request)
    {
        throw new NotImplementedException();
    }
}
