using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

public record FacebookDispatchResult(
    bool IsSuccess,
    string? PostId = null,
    string? ErrorDescription = null,
    bool IsRetryable = false
);

public record FacebookPostSummary(
    string Id,
    string? Message = null,
    DateTimeOffset? CreatedTime = null
);

/// <summary>
/// Low-level HTTP transport adapter port for Facebook Graph API.
/// Target specification: svitlosk-specification/publisher/channels/facebook/
/// </summary>
public interface IFacebookAdapter
{
    Task<FacebookDispatchResult> PublishPostAsync(
        string pageId,
        string text,
        byte[]? imageBytes = null,
        CancellationToken cancellationToken = default);

    Task<FacebookDispatchResult> UpdatePostAsync(
        string postId,
        string text,
        CancellationToken cancellationToken = default);

    Task<FacebookDispatchResult> DeletePostAsync(
        string postId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FacebookPostSummary>> GetRecentPostsAsync(
        string pageId,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
