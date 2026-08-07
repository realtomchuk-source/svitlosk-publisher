// Source: TELEGRAM_ADAPTER_ARCHITECTURE.md
// Section: 1

using System;
using Microsoft.Extensions.Logging;
using SvitloSk.Publisher.Channels;

namespace SvitloSk.Publisher.Adapters.Telegram;

public class TelegramAdapter : IPublicationPort
{
    private readonly ILogger<TelegramAdapter> _logger;

    public TelegramAdapter(ILogger<TelegramAdapter> logger)
    {
        _logger = logger;
    }

    public AcceptedPublication Publish(PublicationRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        _logger.LogInformation("TelegramAdapter publishing request: {RequestId}", request.Id);
        // Stub implementation for production readiness without external dependencies
        return new AcceptedPublication(request.Id, "TelegramAdapter");
    }
}
