using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

public record WhatsAppDispatchResult(
    bool IsSuccess,
    string? MessageId = null,
    string? ErrorDescription = null,
    bool IsRetryable = false,
    int? RetryAfterSeconds = null,
    string? OperationStatus = null
);

public record WhatsAppTailMessage(
    int Order,
    string MessageId,
    string? FullDataId,
    string? Marker,
    string? MarkerType,
    string? MarkerDate,
    int? MarkerSeq,
    string TitleLine,
    string TextWithoutMarker,
    bool HasImage
);

public record WhatsAppTailStateResult(
    bool IsSuccess,
    IReadOnlyList<WhatsAppTailMessage> Messages,
    string? ErrorDescription = null
);

/// <summary>
/// Low-level transport adapter port for WhatsApp Messenger & WhatsApp Channels.
/// Supports text delivery, media/banner dispatching, in-place updates, deletions, and tail-state reading.
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

    Task<WhatsAppDispatchResult> SweepObsoleteForecastsAsync(
        string channelOrChatId,
        string todayDate,
        CancellationToken cancellationToken = default);

    Task<WhatsAppTailStateResult> GetTailStateAsync(
        string channelOrChatId,
        string? todayDdMm = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new WhatsAppTailStateResult(true, Array.Empty<WhatsAppTailMessage>()));
    }
}
