using System;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

public record WhatsAppDispatchResult(
    bool IsSuccess,
    string? MessageId = null,
    string? ErrorDescription = null,
    bool IsRetryable = false,
    int? RetryAfterSeconds = null
);

/// <summary>
/// Low-level transport adapter port for WhatsApp Messenger & WhatsApp Channels.
/// Supports text delivery, media/banner dispatching, in-place updates, and deletions.
/// </summary>
public interface IWhatsAppAdapter
{
    Task<WhatsAppDispatchResult> SendTextMessageAsync(
        string channelOrChatId,
        string text,
        CancellationToken cancellationToken = default);

    Task<WhatsAppDispatchResult> SendMediaMessageAsync(
        string channelOrChatId,
        string caption,
        byte[] mediaBytes,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default);

    Task<WhatsAppDispatchResult> UpdateTextMessageAsync(
        string channelOrChatId,
        string messageId,
        string text,
        CancellationToken cancellationToken = default);

    Task<WhatsAppDispatchResult> DeleteMessageAsync(
        string channelOrChatId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<WhatsAppDispatchResult> CheckChannelAccessAsync(
        string channelOrChatId,
        CancellationToken cancellationToken = default);
}
