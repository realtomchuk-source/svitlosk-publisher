using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Application.Interfaces;

public record TelegramDispatchResult(
    bool IsSuccess, 
    int? MessageId, 
    string? ErrorDescription, 
    bool IsRetryable, 
    int? RetryAfterSeconds = null
);

public interface ITelegramAdapter
{
    Task<TelegramDispatchResult> SendAsync(string chatNameOrId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default);
    Task<TelegramDispatchResult> UpdateAsync(string chatNameOrId, int messageId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default);
    Task<TelegramDispatchResult> DeleteAsync(string chatNameOrId, int messageId, CancellationToken cancellationToken = default);
    Task<TelegramDispatchResult> CloseCommentsAsync(string discussionGroupId, int channelMessageId, CancellationToken cancellationToken = default);
}

public record GraphicOperationPayload(
    string ChatNameOrId,
    string OperationType, // "CREATE", "UPDATE", "DELETE"
    string TerritoryId,
    string ContentHash,
    byte[]? SvgBytes,
    int? TelegramMessageId
);

public interface IGraphicPublisherDispatcher
{
    Task<TelegramDispatchResult> DispatchGraphicAsync(GraphicOperationPayload payload, CancellationToken cancellationToken = default);
}

