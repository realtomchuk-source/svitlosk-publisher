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

        public int UpdateCount { get; private set; }
        public string? LastUpdatedPostId { get; private set; }
        public string? LastUpdatedText { get; private set; }
        public List<FacebookPostSummary> RecentPosts { get; set; } = new();

        public Task<FacebookDispatchResult> UpdatePostAsync(
            string postId,
            string text,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            LastUpdatedPostId = postId;
            LastUpdatedText = text;
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

        public Task<IReadOnlyList<FacebookPostSummary>> GetRecentPostsAsync(
            string pageId,
            int limit = 10,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<FacebookPostSummary>>(RecentPosts);
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

    [Fact]
    public async Task DispatchAsync_PreFlightReconciliation_AdoptsExistingPostAndUpdates_InsteadOfPublishing()
    {
        var testAdapter = new TestFacebookAdapter();
        testAdapter.RecentPosts.Add(new FacebookPostSummary(
            Id: "page_123_existing_planned",
            Message: "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 16.09.2026\n\nСтарокостянтинівська міська територіальна громада\n• вул. Миру"
        ));

        var pipeline = new FacebookPipeline(testAdapter, "page_123");

        var createDecision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "fb_planned",
            TargetHash: "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 16.09.2026\n\nСтарокостянтинівська міська територіальна громада\n• вул. Миру (09:00–17:00)",
            ScheduleDate: "16.09.2026",
            Type: PublicationType.Text
        );

        var result = await pipeline.DispatchAsync(new[] { createDecision });

        Assert.True(result.IsSuccess);
        // Pre-flight check should adopt the existing post and call Update, NOT Publish!
        Assert.Equal(0, testAdapter.PublishCount);
        Assert.Equal(1, testAdapter.UpdateCount);
        Assert.Equal("page_123_existing_planned", testAdapter.LastUpdatedPostId);
        Assert.Equal("page_123_existing_planned", result.Results[0].ExternalMessageId);
    }

    [Fact]
    public async Task DispatchAsync_PreFlightReconciliation_PublishesNormally_WhenNoMatchingPostExists()
    {
        var testAdapter = new TestFacebookAdapter();
        testAdapter.RecentPosts.Add(new FacebookPostSummary(
            Id: "page_123_yesterday_post",
            Message: "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 15.09.2026\n\nВчорашній пост"
        ));

        var pipeline = new FacebookPipeline(testAdapter, "page_123");

        var createDecision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "fb_planned",
            TargetHash: "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 16.09.2026\n\nСьогоднішній пост",
            ScheduleDate: "16.09.2026",
            Type: PublicationType.Text
        );

        var result = await pipeline.DispatchAsync(new[] { createDecision });

        Assert.True(result.IsSuccess);
        // Does not match 15.09.2026, so publishes brand new post
        Assert.Equal(1, testAdapter.PublishCount);
        Assert.Equal(0, testAdapter.UpdateCount);
        Assert.Equal("new_graphic_id", result.Results[0].ExternalMessageId);
    }

    [Fact]
    public void FindMatchingPost_RejectsPost_WhenDateDiffers()
    {
        var recentPosts = new List<FacebookPostSummary>
        {
            new("post_yesterday", "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 16.09.2026\n\nСтарокостянтинів")
        };

        var decision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Ephemeral,
            Guid.NewGuid(),
            "fb_planned",
            ScheduleDate: "2026-09-17"
        );

        var match = FacebookPipeline.FindMatchingPost(recentPosts, null, decision, "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 17.09.2026\n\nСтарокостянтинів");

        Assert.Null(match);
    }

    [Fact]
    public void FindMatchingPost_Matches_WhenDateInIsoMatchesDdMmYyyyInPost()
    {
        var recentPosts = new List<FacebookPostSummary>
        {
            new("post_today", "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 17.09.2026\n\nСтарокостянтинів")
        };

        var decision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Ephemeral,
            Guid.NewGuid(),
            "fb_planned",
            ScheduleDate: "2026-09-17"
        );

        var match = FacebookPipeline.FindMatchingPost(recentPosts, null, decision, "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 17.09.2026\n\nСтарокостянтинів");

        Assert.NotNull(match);
        Assert.Equal("post_today", match.Id);
    }

    [Fact]
    public void FindMatchingPost_ReturnsNull_WhenNoDatePresent()
    {
        var recentPosts = new List<FacebookPostSummary>
        {
            new("post_1", "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ\n\nТекст без дати")
        };

        var decision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Ephemeral,
            Guid.NewGuid(),
            "fb_planned",
            ScheduleDate: null
        );

        var match = FacebookPipeline.FindMatchingPost(recentPosts, null, decision, "ПЛАНОВІ ЗНЕСТРУМЛЕННЯ\n\nТекст без дати");

        Assert.Null(match);
    }

    [Fact]
    public async Task DispatchAsync_PreFlightSweep_DeletesObsoleteTomorrowForecast_WhenDateIsTodayOrPast()
    {
        var adapter = new TestFacebookAdapter
        {
            RecentPosts = new List<FacebookPostSummary>
            {
                new("post_old_forecast", "ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА\n18.09.2026\nСтарокостянтинів"),
                new("post_yesterday_journal", "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n17.09.2026 ЧЕТВЕР\nСтарокостянтинів")
            }
        };

        var pipeline = new FacebookPipeline(adapter, "test_page_123", new TestGraphicRasterizer());

        var decisions = new List<EditorialDecision>
        {
            new(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "fb_planned", "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n18.09.2026 П'ЯТНИЦЯ", ScheduleDate: "2026-09-18")
        };

        var result = await pipeline.DispatchAsync(decisions);

        Assert.True(result.IsSuccess);
        // Obsolete forecast was deleted; yesterday's journal was preserved!
        Assert.Equal(1, adapter.DeleteCount);
        Assert.Equal("post_old_forecast", adapter.LastDeletedPostId);
    }

    [Fact]
    public async Task DispatchAsync_PreFlightSweep_PreservesFutureTomorrowForecast()
    {
        var adapter = new TestFacebookAdapter
        {
            RecentPosts = new List<FacebookPostSummary>
            {
                new("post_future_forecast", "ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА\n19.09.2026\nСтарокостянтинів"),
                new("post_yesterday_journal", "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n17.09.2026 ЧЕТВЕР\nСтарокостянтинів")
            }
        };

        var pipeline = new FacebookPipeline(adapter, "test_page_123", new TestGraphicRasterizer());

        var decisions = new List<EditorialDecision>
        {
            new(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "fb_planned", "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n18.09.2026 П'ЯТНИЦЯ", ScheduleDate: "2026-09-18")
        };

        var result = await pipeline.DispatchAsync(decisions);

        Assert.True(result.IsSuccess);
        // Neither future forecast nor yesterday's journal is deleted
        Assert.Equal(0, adapter.DeleteCount);
    }

    [Fact]
    public void FindMatchingPost_MatchesGraphicPost_WhenDateMatches()
    {
        var recentPosts = new List<FacebookPostSummary>
        {
            new("post_graphic_today", "ГРАФІК ЗНЕСТРУМЛЕНЬ\n18.09.2026 п'ятниця, Старокостянтинівська міська територіальна громада\n\nОпубліковано детальний 12-підчерговий графік")
        };

        var decision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "graphic",
            ScheduleDate: "2026-09-18",
            Type: PublicationType.Graphic
        );

        var match = FacebookPipeline.FindMatchingPost(recentPosts, null, decision, "ГРАФІК ЗНЕСТРУМЛЕНЬ\n18.09.2026 п'ятниця");

        Assert.NotNull(match);
        Assert.Equal("post_graphic_today", match.Id);
    }

    [Fact]
    public async Task DispatchAsync_GraphicPost_DeletesExistingMatchingPost_BeforePublishing()
    {
        var adapter = new TestFacebookAdapter
        {
            RecentPosts = new List<FacebookPostSummary>
            {
                new("existing_graphic_post", "ГРАФІК ЗНЕСТРУМЛЕНЬ\n18.09.2026 п'ятниця, Старокостянтинівська міська територіальна громада\n\nОпубліковано детальний 12-підчерговий графік")
            }
        };

        var pipeline = new FacebookPipeline(adapter, "test_page_123", new TestGraphicRasterizer());

        var decision = new EditorialDecision(
            DecisionResult.Create,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "graphic",
            ScheduleDate: "2026-09-18",
            Type: PublicationType.Graphic,
            SvgBytes: new byte[] { 60, 115, 118, 103, 62 }
        );

        var result = await pipeline.DispatchAsync(new[] { decision });

        Assert.True(result.IsSuccess);
        // Old graphic post was deleted before publishing new one to prevent duplicates!
        Assert.Equal(1, adapter.DeleteCount);
        Assert.Equal("existing_graphic_post", adapter.LastDeletedPostId);
        Assert.Equal(1, adapter.PublishCount);
        Assert.Equal("new_graphic_id", result.Results[0].ExternalMessageId);
    }

    [Fact]
    public void IsNoOutagesPost_CorrectlyIdentifiesNoOutagesVsOutages()
    {
        string noOutages = "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n19.09.2026 субота\n\nПланові знеструмлення: відсутні\nАварійні знеструмлення: відсутні";
        string withEmergency = "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n19.09.2026 субота\n\nПланові знеструмлення: відсутні\nАварійні знеструмлення: м. Старокостянтинів";

        Assert.True(FacebookPipeline.IsNoOutagesPost(noOutages));
        Assert.False(FacebookPipeline.IsNoOutagesPost(withEmergency));
    }

    [Fact]
    public async Task DispatchAsync_BannerStateTransition_RecreatesPost_WhenEmergencyOutagesAppear()
    {
        var adapter = new TestFacebookAdapter
        {
            RecentPosts = new List<FacebookPostSummary>
            {
                new("morning_no_outages_post", "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n19.09.2026 субота, Старокостянтинівська міська територіальна громада\n\nПланові знеструмлення: відсутні\nАварійні знеструмлення: відсутні\n\nОстаннє оновлення: 06:25")
            }
        };

        var pipeline = new FacebookPipeline(adapter, "test_page_123", new TestGraphicRasterizer());

        var updateDecision = new EditorialDecision(
            DecisionResult.Update,
            PublicationClassification.Persistent,
            Guid.NewGuid(),
            "fb_planned",
            TargetHash: "ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n19.09.2026 субота, Старокостянтинівська міська територіальна громада\n\nПланові знеструмлення: відсутні\nАварійні знеструмлення: м. Старокостянтинів\n\nАВАРІЙНІ ЗНЕСТРУМЛЕННЯ\n\nм. Старокостянтинів\nвул. Грушевського\n\nОстаннє оновлення: 12:26",
            ExternalMessageId: "morning_no_outages_post",
            ScheduleDate: "2026-09-19"
        );

        var result = await pipeline.DispatchAsync(new[] { updateDecision });

        Assert.True(result.IsSuccess);
        // Banner changed from "ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ" to active outages DayHeader!
        // Previous post must be deleted and new post published with updated photo!
        Assert.Equal(1, adapter.DeleteCount);
        Assert.Equal("morning_no_outages_post", adapter.LastDeletedPostId);
        Assert.Equal(1, adapter.PublishCount);
        Assert.Equal("new_graphic_id", result.Results[0].ExternalMessageId);
    }
}


