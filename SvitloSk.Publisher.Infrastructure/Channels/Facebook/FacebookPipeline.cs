using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Graphics;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

/// <summary>
/// Dedicated Facebook Channel Pipeline implementing Clean Architecture port IChannelPipeline.
/// Encapsulates platform-specific Facebook Graph API dispatch sequencing, photo post lifecycle
/// (delete-and-recreate for image updates), Facebook 1200x630 banner assembly, and clean text formatting.
/// </summary>
public class FacebookPipeline : IChannelPipeline
{
    private readonly IFacebookAdapter? _facebookAdapter;
    private readonly string? _pageId;
    private readonly IGraphicRasterizer _rasterizer;
    private readonly IBannerGraphicAssembly _bannerAssembly;

    public string ChannelName => "Facebook";

    public FacebookPipeline(
        IFacebookAdapter? facebookAdapter = null,
        string? pageId = null,
        IGraphicRasterizer? rasterizer = null,
        IBannerGraphicAssembly? bannerAssembly = null)
    {
        _facebookAdapter = facebookAdapter;
        _pageId = pageId;
        _rasterizer = rasterizer ?? new SvgSkiaRasterizer();
        _bannerAssembly = bannerAssembly ?? new BannerGraphicAssembly();
    }

    public async Task<BatchDispatchResult> DispatchAsync(
        IReadOnlyList<EditorialDecision> decisions,
        CancellationToken cancellationToken = default)
    {
        if (decisions == null)
            throw new ArgumentNullException(nameof(decisions));

        if (string.IsNullOrWhiteSpace(_pageId) || _facebookAdapter == null)
        {
            Console.WriteLine("[INFO] Facebook channel is not configured or in stub mode, skipping dispatch.");
            return new BatchDispatchResult(
                IsSuccess: true,
                TotalProcessed: 0,
                TotalSuccessful: 0,
                FatalErrorDescription: null,
                Results: Array.Empty<DispatchResultRecord>()
            );
        }

        var results = new List<DispatchResultRecord>();
        int totalProcessed = 0;
        int totalSuccessful = 0;

        foreach (var decision in decisions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Technical system_status is embedded in daily digest and not posted as a standalone spam post on Facebook
            if (string.Equals(decision.TerritoryIdentifier, "system_status", StringComparison.OrdinalIgnoreCase))
            {
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    isSuccess: true,
                    externalMessageId: decision.ExternalMessageId ?? "fb_virtual_system_status",
                    errorDescription: null,
                    publicationType: decision.Type.ToString()
                ));
                totalSuccessful++;
                continue;
            }

            // 2. NoAction / Keep decisions
            if (decision.DecisionResult is DecisionResult.NoAction or DecisionResult.Keep)
            {
                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    decision.DecisionResult.ToString(),
                    isSuccess: true,
                    externalMessageId: decision.ExternalMessageId,
                    errorDescription: null,
                    publicationType: decision.Type.ToString()
                ));
                totalSuccessful++;
                continue;
            }

            totalProcessed++;

            // 3. Delete decisions
            if (decision.DecisionResult == DecisionResult.Delete)
            {
                if (!string.IsNullOrEmpty(decision.ExternalMessageId))
                {
                    var delRes = await _facebookAdapter.DeletePostAsync(decision.ExternalMessageId, cancellationToken).ConfigureAwait(false);
                    if (delRes.IsSuccess) totalSuccessful++;

                    results.Add(new DispatchResultRecord(
                        decision.PublicationId,
                        decision.TerritoryIdentifier,
                        "Delete",
                        isSuccess: delRes.IsSuccess,
                        externalMessageId: null,
                        errorDescription: delRes.ErrorDescription,
                        publicationType: decision.Type.ToString()
                    ));
                }
                else
                {
                    results.Add(new DispatchResultRecord(
                        decision.PublicationId,
                        decision.TerritoryIdentifier,
                        "Delete",
                        isSuccess: true,
                        externalMessageId: null,
                        errorDescription: null,
                        publicationType: decision.Type.ToString()
                    ));
                    totalSuccessful++;
                }
                continue;
            }

            // 4. Create decisions
            if (decision.DecisionResult == DecisionResult.Create)
            {
                FacebookDispatchResult pubRes;

                if (decision.Type == PublicationType.Graphic)
                {
                    byte[]? imageBytes = null;
                    if (decision.SvgBytes != null && decision.SvgBytes.Length > 0)
                    {
                        imageBytes = _rasterizer.RasterizeSvgToPng(decision.SvgBytes, 1080, 1080);
                    }

                    string caption = FacebookContentFormatter.FormatGraphicCaption(decision.ScheduleDate ?? DateTime.UtcNow.ToString("dd.MM.yyyy"));
                    pubRes = await _facebookAdapter.PublishPostAsync(_pageId, caption, imageBytes, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    string cleanText = FacebookContentFormatter.StripHtml(decision.TargetHash);
                    byte[]? bannerBytes = ResolveFacebookBanner(decision);

                    pubRes = await _facebookAdapter.PublishPostAsync(_pageId, cleanText, bannerBytes, cancellationToken).ConfigureAwait(false);
                }

                if (pubRes.IsSuccess) totalSuccessful++;

                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    "Create",
                    isSuccess: pubRes.IsSuccess,
                    externalMessageId: pubRes.PostId,
                    errorDescription: pubRes.ErrorDescription,
                    publicationType: decision.Type.ToString()
                ));
                continue;
            }

            // 5. Update decisions
            if (decision.DecisionResult == DecisionResult.Update)
            {
                FacebookDispatchResult updRes;

                // Photo posts cannot have their image edited in Facebook Graph API -> Re-publish (Delete old + Create new)
                if (decision.Type == PublicationType.Graphic || HasBanner(decision))
                {
                    if (!string.IsNullOrEmpty(decision.ExternalMessageId))
                    {
                        await _facebookAdapter.DeletePostAsync(decision.ExternalMessageId, cancellationToken).ConfigureAwait(false);
                    }

                    if (decision.Type == PublicationType.Graphic)
                    {
                        byte[]? imageBytes = null;
                        if (decision.SvgBytes != null && decision.SvgBytes.Length > 0)
                        {
                            imageBytes = _rasterizer.RasterizeSvgToPng(decision.SvgBytes, 1080, 1080);
                        }

                        string caption = FacebookContentFormatter.FormatGraphicCaption(decision.ScheduleDate ?? DateTime.UtcNow.ToString("dd.MM.yyyy"));
                        updRes = await _facebookAdapter.PublishPostAsync(_pageId, caption, imageBytes, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        string cleanText = FacebookContentFormatter.StripHtml(decision.TargetHash);
                        byte[]? bannerBytes = ResolveFacebookBanner(decision);
                        updRes = await _facebookAdapter.PublishPostAsync(_pageId, cleanText, bannerBytes, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Pure text post can be edited in place
                    string cleanText = FacebookContentFormatter.StripHtml(decision.TargetHash);
                    updRes = await _facebookAdapter.UpdatePostAsync(decision.ExternalMessageId ?? string.Empty, cleanText, cancellationToken).ConfigureAwait(false);
                }

                if (updRes.IsSuccess) totalSuccessful++;

                results.Add(new DispatchResultRecord(
                    decision.PublicationId,
                    decision.TerritoryIdentifier,
                    "Update",
                    isSuccess: updRes.IsSuccess,
                    externalMessageId: updRes.PostId ?? decision.ExternalMessageId,
                    errorDescription: updRes.ErrorDescription,
                    publicationType: decision.Type.ToString()
                ));
                continue;
            }
        }

        bool overallSuccess = results.All(r => r.IsSuccess);
        string? fatalError = overallSuccess ? null : "[FacebookPipeline] One or more Facebook operations failed.";

        return new BatchDispatchResult(
            IsSuccess: overallSuccess,
            TotalProcessed: totalProcessed,
            TotalSuccessful: totalSuccessful,
            FatalErrorDescription: fatalError,
            Results: results
        );
    }

    private static bool HasBanner(EditorialDecision decision)
    {
        return decision.GraphicBytes != null ||
               string.Equals(decision.TerritoryIdentifier, "journal_header", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(decision.TerritoryIdentifier, "tomorrow_separator", StringComparison.OrdinalIgnoreCase);
    }

    private byte[]? ResolveFacebookBanner(EditorialDecision decision)
    {
        try
        {
            if (string.Equals(decision.TerritoryIdentifier, "journal_header", StringComparison.OrdinalIgnoreCase))
            {
                byte[] svg = _bannerAssembly.AssembleFacebookDayHeaderSvg(decision.ScheduleDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
                return _rasterizer.RasterizeSvgToPng(svg, 1200, 630);
            }

            if (string.Equals(decision.TerritoryIdentifier, "tomorrow_separator", StringComparison.OrdinalIgnoreCase))
            {
                byte[] svg = _bannerAssembly.AssembleFacebookTomorrowHeaderSvg(decision.ScheduleDate ?? DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"));
                return _rasterizer.RasterizeSvgToPng(svg, 1200, 630);
            }

            return decision.GraphicBytes;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN][Facebook] Failed to render specialized 1200x630 banner: {ex.Message}. Falling back to default.");
            return decision.GraphicBytes;
        }
    }
}
