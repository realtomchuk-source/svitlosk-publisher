using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.Facebook;

public class FacebookPipelineTests
{
    private class TestFacebookAdapter : IFacebookAdapter
    {
        public int DeleteCount { get; private set; }
        public int PublishCount { get; private set; }
        public string? LastDeletedPostId { get; private set; }
        public string? LastPublishedText { get; private set; }
        public byte[]? LastPublishedImageBytes { get; private set; }

        public Task<FacebookDispatchResult> PublishPostAsync(
            string pageId,
            string text,
            byte[]? imageBytes = null,
            CancellationToken cancellationToken = default)
        {
            PublishCount++;
            LastPublishedText = text;
            LastPublishedImageBytes = imageBytes;
            return Task.FromResult(new FacebookDispatchResult(true, PostId: "new_graphic_id"));
        }

        public Task<FacebookDispatchResult> UpdatePostAsync(
            string postId,
            string text,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new FacebookDispatchResult(true, PostId: postId));
        }

        public Task<FacebookDispatchResult> DeletePostAsync(
            string postId,
            CancellationToken cancellationToken = default)
        {
            DeleteCount++;
            LastDeletedPostId = postId;
            return Task.FromResult(new FacebookDispatchResult(true, PostId: postId));
        }
    }

    private class TestGraphicRasterizer : IGraphicRasterizer
    {
        public byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 1000, int height = 650)
        {
            return new byte[] { 1, 2, 3 };
        }
    }

    [Fact]
    public async Task DispatchAsync_Unconfigured_ReturnsSuccessWithZeroProcessed()
    {
        var pipeline = new FacebookPipeline(facebookAdapter: null, pageId: null);

        var result = await pipeline.DispatchAsync(new List<EditorialDecision>
        {
            new(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "starokostiantyniv", "content")
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.TotalProcessed);
    }

    [Fact]
    public async Task DispatchAsync_DryRunAdapter_SimulatesCreateAndUpdate()
    {
        var dryRunAdapter = new FacebookDryRunAdapter();
        var pipeline = new FacebookPipeline(dryRunAdapter, "test_page_123");

        var decisions = new List<EditorialDecision>
        {
            new(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "starokostiantyniv", "<b>м. Старокостянтинів</b>"),
            new(DecisionResult.Update, PublicationClassification.Persistent, Guid.NewGuid(), "kapustynskyi", "<b>Капустинський</b>", ExternalMessageId: "test_page_123_post_456"),
            new(DecisionResult.Delete, PublicationClassification.Persistent, Guid.NewGuid(), "old_post", null, ExternalMessageId: "test_page_123_post_789"),
            new(DecisionResult.Create, PublicationClassification.Ephemeral, Guid.NewGuid(), "system_status", "Останнє оновлення")
        };

        var result = await pipeline.DispatchAsync(decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.TotalProcessed); // system_status is skipped from totalProcessed
        Assert.Equal(4, result.Results.Count); // all 4 records present in result

        var createRec = result.Results[0];
        Assert.Equal("Create", createRec.DecisionResult);
        Assert.True(createRec.IsSuccess);
        Assert.StartsWith("test_page_123_sim_", createRec.ExternalMessageId);

        var updateRec = result.Results[1];
        Assert.Equal("Update", updateRec.DecisionResult);
        Assert.True(updateRec.IsSuccess);
        Assert.Equal("test_page_123_post_456", updateRec.ExternalMessageId);

        var deleteRec = result.Results[2];
        Assert.Equal("Delete", deleteRec.DecisionResult);
        Assert.True(deleteRec.IsSuccess);
        Assert.Null(deleteRec.ExternalMessageId);

        var statusRec = result.Results[3];
        Assert.True(statusRec.IsSuccess);
    }

    [Fact]
    public async Task DispatchAsync_GraphicPhotoUpdate_TriggersDeleteAndRecreate()
    {
        var testAdapter = new TestFacebookAdapter();
        var testRasterizer = new TestGraphicRasterizer();

        var pipeline = new FacebookPipeline(testAdapter, "page_123", testRasterizer);

        var graphicDecision = new EditorialDecision(
            DecisionResult.Update,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "Старокостянтинівська МТГ",
            TargetHash: "new_hash",
            ExternalMessageId: "old_graphic_id",
            Type: PublicationType.Graphic,
            SvgBytes: new byte[] { 60, 115, 118, 103, 62 } // <svg>
        );

        var result = await pipeline.DispatchAsync(new[] { graphicDecision });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, testAdapter.DeleteCount);
        Assert.Equal("old_graphic_id", testAdapter.LastDeletedPostId);
        Assert.Equal(1, testAdapter.PublishCount);
        Assert.Equal("new_graphic_id", result.Results[0].ExternalMessageId);
    }
}
