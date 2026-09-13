using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

/// <summary>
/// Facebook Channel Pipeline Skeleton (Phase 1 Stub).
/// Safe placeholder implementing Clean Architecture port IChannelPipeline.
/// Production implementation will be activated in Phase 2 after Telegram channel verification.
/// </summary>
public class FacebookPipeline : IChannelPipeline
{
    private readonly IFacebookAdapter? _facebookAdapter;
    private readonly string? _pageId;

    public string ChannelName => "Facebook";

    public FacebookPipeline(IFacebookAdapter? facebookAdapter = null, string? pageId = null)
    {
        _facebookAdapter = facebookAdapter;
        _pageId = pageId;
    }

    public Task<BatchDispatchResult> DispatchAsync(
        IReadOnlyList<EditorialDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_pageId) || _facebookAdapter == null)
        {
            Console.WriteLine("[INFO] Facebook channel is not configured or in stub mode, skipping dispatch.");
            return Task.FromResult(new BatchDispatchResult(
                IsSuccess: true,
                TotalProcessed: 0,
                TotalSuccessful: 0,
                FatalErrorDescription: null,
                Results: Array.Empty<DispatchResultRecord>()
            ));
        }

        // Implementation of Graph API dispatching logic will be added in Phase 2
        return Task.FromResult(new BatchDispatchResult(
            IsSuccess: true,
            TotalProcessed: 0,
            TotalSuccessful: 0,
            FatalErrorDescription: null,
            Results: Array.Empty<DispatchResultRecord>()
        ));
    }
}
