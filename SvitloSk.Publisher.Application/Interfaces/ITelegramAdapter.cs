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
}
