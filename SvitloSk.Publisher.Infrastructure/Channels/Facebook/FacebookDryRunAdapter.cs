using System;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

/// <summary>
/// Dry-run simulation adapter for Facebook Graph API.
/// Used during development, verification cycles, and offline testing without live Facebook API credentials.
/// </summary>
public class FacebookDryRunAdapter : IFacebookAdapter
{
    public Task<FacebookDispatchResult> PublishPostAsync(
        string pageId,
        string text,
        byte[]? imageBytes = null,
        CancellationToken cancellationToken = default)
    {
        string simPostId = $"{pageId}_sim_{Guid.NewGuid():N}";
        Console.WriteLine($"[DRY-RUN][Facebook] PublishPost -> Page: '{pageId}', HasImage: {imageBytes != null}, TextLength: {text.Length}, SimPostId: {simPostId}");
        return Task.FromResult(new FacebookDispatchResult(
            IsSuccess: true,
            PostId: simPostId
        ));
    }

    public Task<FacebookDispatchResult> UpdatePostAsync(
        string postId,
        string text,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DRY-RUN][Facebook] UpdatePost -> PostId: '{postId}', NewTextLength: {text.Length}");
        return Task.FromResult(new FacebookDispatchResult(
            IsSuccess: true,
            PostId: postId
        ));
    }

    public Task<FacebookDispatchResult> DeletePostAsync(
        string postId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DRY-RUN][Facebook] DeletePost -> PostId: '{postId}'");
        return Task.FromResult(new FacebookDispatchResult(
            IsSuccess: true,
            PostId: postId
        ));
    }

    public Task<IReadOnlyList<FacebookPostSummary>> GetRecentPostsAsync(
        string pageId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DRY-RUN][Facebook] GetRecentPosts -> Page: '{pageId}', Limit: {limit}");
        return Task.FromResult<IReadOnlyList<FacebookPostSummary>>(Array.Empty<FacebookPostSummary>());
    }
}
