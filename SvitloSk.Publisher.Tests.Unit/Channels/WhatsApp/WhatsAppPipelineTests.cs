using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.WhatsApp;

public class WhatsAppPipelineTests
{
    [Fact]
    public void TC_OrderDecisionsWithCityPriority_GuaranteesCityFirst_AndSystemStatusAtTail()
    {
        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "system_status"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "pashkivtsi"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "journal_header"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "starokostiantyniv"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_pashkivtsi"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_separator"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_header"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_starokostiantyniv"),
            new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, TerritoryIdentifier: "yesterday_cleanup")
        };

        var ordered = WhatsAppPipeline.OrderDecisionsWithCityPriority(decisions);

        Assert.Equal("yesterday_cleanup", ordered[0].TerritoryIdentifier); // 0. Rollover cleanup
        Assert.Equal("journal_header", ordered[1].TerritoryIdentifier);   // 1. Journal Header
        Assert.Equal("starokostiantyniv", ordered[2].TerritoryIdentifier); // 2. City (Priority #1)
        Assert.Equal("pashkivtsi", ordered[3].TerritoryIdentifier);       // 3. Today District
        Assert.Equal("tomorrow_separator", ordered[4].TerritoryIdentifier); // 4. Tomorrow Banner
        Assert.Equal("tomorrow_header", ordered[5].TerritoryIdentifier);   // 5. Tomorrow Summary Text
        Assert.Equal("tomorrow_starokostiantyniv", ordered[6].TerritoryIdentifier); // 6. Tomorrow City
        Assert.Equal("tomorrow_pashkivtsi", ordered[7].TerritoryIdentifier); // 7. Tomorrow District
        Assert.Equal("system_status", ordered[8].TerritoryIdentifier);    // 9. Tail
    }

    [Fact]
    public async Task TC_DispatchAsync_ExecutesBatch_WithDryRunAdapter()
    {
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var pipeline = new WhatsAppPipeline(dryRunAdapter, "test_channel_id");

        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "journal_header", TargetHash: "<b>Заголовок дня</b>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "starokostiantyniv", TargetHash: "<b>Місто Старокостянтинів</b>\n<blockquote><b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ (10:00–14:00)</b>\nвул. Миру 1, 2, 3</blockquote>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "system_status", TargetHash: "<b>Останнє оновлення:</b> 14:15")
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, result.TotalSuccessful);
        Assert.All(result.Results, r => Assert.True(r.IsSuccess));
        Assert.All(result.Results, r => Assert.NotNull(r.ExternalMessageId));

        Assert.Equal(3, dryRunAdapter.DispatchedMessages.Count);
    }

    [Fact]
    public async Task TC_DispatchAsync_DispatchesTomorrowForecasts_AndDispatchesSystemStatusAtTail()
    {
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var pipeline = new WhatsAppPipeline(dryRunAdapter, "test_channel_id");

        var bannerBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };

        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "journal_header", TargetHash: "<b>Заголовок дня</b>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, TerritoryIdentifier: "starokostiantyniv", TargetHash: "<b>м. Старокостянтинів</b>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_separator", GraphicBytes: bannerBytes, TargetHash: ""),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_header", TargetHash: "<b>ПРОГНОЗ НА ЗАВТРА</b> • 27.09.2026\n\n<b>Планові знеструмлення:</b> відсутні\n<b>Аварійні знеструмлення:</b> відсутні"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_starokostiantyniv", TargetHash: "<b>м. Старокостянтинів</b>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "tomorrow_krasnosilskyi", TargetHash: "<b>Красносілка</b>"),
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Ephemeral, TerritoryIdentifier: "system_status", TargetHash: "<b>Останнє оновлення</b>")
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.TotalProcessed);
        Assert.Equal(7, result.TotalSuccessful);
        Assert.Equal(7, dryRunAdapter.DispatchedMessages.Count);
        
        var tomorrowSeparatorRecord = result.Results.First(r => r.TerritoryIdentifier == "tomorrow_separator");
        Assert.NotNull(tomorrowSeparatorRecord.ExternalMessageId);
        Assert.True(tomorrowSeparatorRecord.IsSuccess);

        var tomorrowHeaderRecord = result.Results.First(r => r.TerritoryIdentifier == "tomorrow_header");
        Assert.NotNull(tomorrowHeaderRecord.ExternalMessageId);
        Assert.True(tomorrowHeaderRecord.IsSuccess);

        var statusRecord = result.Results.First(r => r.TerritoryIdentifier == "system_status");
        Assert.NotNull(statusRecord.ExternalMessageId);
        Assert.StartsWith("wa_mock_", statusRecord.ExternalMessageId);

        // Verification that banner was sent as MEDIA
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.StartsWith("[MEDIA]") && m.Contains("Bytes: 4"));
    }

    [Fact]
    public async Task TC_DispatchAsync_DecouplesBannerAndText_ForJournalHeaderWithGraphic()
    {
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var pipeline = new WhatsAppPipeline(dryRunAdapter, "test_channel_id");

        var graphicBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // Mock PNG
        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(
                DecisionResult.Create,
                PublicationClassification.Persistent,
                TerritoryIdentifier: "journal_header",
                TargetHash: "<b>Планові знеструмлення:</b> м. Старокостянтинів",
                GraphicBytes: graphicBytes
            )
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal(1, result.TotalSuccessful);

        // 2 messages were sent to WhatsApp: 1 MEDIA (banner) and 1 TEXT (caption/summary)
        Assert.Equal(2, dryRunAdapter.DispatchedMessages.Count);
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.StartsWith("[MEDIA]"));
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.StartsWith("[TEXT]"));

        // Result ID points to the TEXT message (which begins with wa_mock_, not wa_mock_media_)
        Assert.NotNull(result.Results[0].ExternalMessageId);
        Assert.StartsWith("wa_mock_", result.Results[0].ExternalMessageId);
        Assert.DoesNotContain("media", result.Results[0].ExternalMessageId);
    }

    [Fact]
    public async Task TC_DispatchAsync_InPlaceUpdate_CallsUpdateTextMessageAsync()
    {
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var pipeline = new WhatsAppPipeline(dryRunAdapter, "test_channel_id");

        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(
                DecisionResult.Update,
                PublicationClassification.Persistent,
                TerritoryIdentifier: "journal_header",
                ExternalMessageId: "msg_existing_header_123",
                TargetHash: "<b>Планові знеструмлення:</b> м. Старокостянтинів, с. Грибенинка"
            )
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal("msg_existing_header_123", result.Results[0].ExternalMessageId);

        // DryRun recorded UPDATE for that message ID
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.Contains("[UPDATE]") && m.Contains("msg_existing_header_123"));
    }

    [Fact]
    public async Task TC_DispatchAsync_SystemStatus_PerformsDeleteAndCreateRollover()
    {
        var dryRunAdapter = new WhatsAppDryRunAdapter();
        var pipeline = new WhatsAppPipeline(dryRunAdapter, "test_channel_id");

        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(
                DecisionResult.Delete,
                PublicationClassification.Ephemeral,
                TerritoryIdentifier: "system_status",
                ExternalMessageId: "old_status_msg_999"
            ),
            new EditorialDecision(
                DecisionResult.Create,
                PublicationClassification.Ephemeral,
                TerritoryIdentifier: "system_status",
                TargetHash: "<b>Останнє оновлення:</b> 15:30"
            )
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.TotalProcessed);
        Assert.Equal(2, result.TotalSuccessful);

        // DryRun recorded DELETE for old message and TEXT for new message
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.Contains("[DELETE]") && m.Contains("old_status_msg_999"));
        Assert.Contains(dryRunAdapter.DispatchedMessages, m => m.Contains("[TEXT]"));
    }

    [Fact]
    public async Task TC_DispatchAsync_WhenUpdateFails_FallsBackToDeleteAndCreateRollover()
    {
        var mockAdapter = new FallbackMockWhatsAppAdapter();
        var pipeline = new WhatsAppPipeline(mockAdapter, "test_channel_id");

        var decisions = new List<EditorialDecision>
        {
            new EditorialDecision(
                DecisionResult.Update,
                PublicationClassification.Persistent,
                TerritoryIdentifier: "starokostiantyniv",
                ExternalMessageId: "wa_old_msg_123",
                TargetHash: "<b>м. Старокостянтинів</b>\n- оновлений список"
            )
        };

        var result = await pipeline.DispatchAsync(decisions, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(mockAdapter.UpdateAttempted);
        Assert.True(mockAdapter.DeleteCalled);
        Assert.True(mockAdapter.SendCalled);
        Assert.Equal("wa_new_msg_456", result.Results[0].ExternalMessageId);
    }

    private class FallbackMockWhatsAppAdapter : IWhatsAppAdapter
    {
        public bool UpdateAttempted { get; private set; }
        public bool DeleteCalled { get; private set; }
        public bool SendCalled { get; private set; }

        public Task<WhatsAppDispatchResult> SendTextMessageAsync(string channelOrChatId, string text, CancellationToken cancellationToken = default)
        {
            SendCalled = true;
            return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: "wa_new_msg_456"));
        }

        public Task<WhatsAppDispatchResult> SendMediaMessageAsync(string channelOrChatId, string caption, byte[] mediaBytes, string mimeType = "image/png", CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: "wa_new_media_456"));
        }

        public Task<WhatsAppDispatchResult> UpdateTextMessageAsync(string channelOrChatId, string messageId, string text, CancellationToken cancellationToken = default)
        {
            UpdateAttempted = true;
            // Simulate expired 30-minute window error
            return Task.FromResult(new WhatsAppDispatchResult(false, ErrorDescription: "Message is too old to be modified (30 min window expired)."));
        }

        public Task<WhatsAppDispatchResult> DeleteMessageAsync(string channelOrChatId, string messageId, CancellationToken cancellationToken = default)
        {
            DeleteCalled = true;
            return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: messageId));
        }

        public Task<WhatsAppDispatchResult> CheckChannelAccessAsync(string channelOrChatId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WhatsAppDispatchResult(true));
        }
    }
}
