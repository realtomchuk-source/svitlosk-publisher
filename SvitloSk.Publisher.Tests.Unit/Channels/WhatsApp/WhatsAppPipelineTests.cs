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
            new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, TerritoryIdentifier: "yesterday_cleanup")
        };

        var ordered = WhatsAppPipeline.OrderDecisionsWithCityPriority(decisions);

        Assert.Equal("yesterday_cleanup", ordered[0].TerritoryIdentifier); // 0. Rollover cleanup
        Assert.Equal("journal_header", ordered[1].TerritoryIdentifier);   // 1. Journal Header
        Assert.Equal("starokostiantyniv", ordered[2].TerritoryIdentifier); // 2. City (Priority #1)
        Assert.Equal("pashkivtsi", ordered[3].TerritoryIdentifier);       // 3. District
        Assert.Equal("tomorrow_pashkivtsi", ordered[4].TerritoryIdentifier); // 4. Tomorrow forecast
        Assert.Equal("system_status", ordered[5].TerritoryIdentifier);    // 5. Tail
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
        Assert.Equal(2, result.TotalProcessed);
        Assert.Equal(3, result.TotalSuccessful);
        Assert.All(result.Results, r => Assert.True(r.IsSuccess));
        Assert.All(result.Results, r => Assert.NotNull(r.ExternalMessageId));

        // Check dry run adapter recorded messages (system_status is virtualized and not dispatched as standalone message)
        Assert.Equal(2, dryRunAdapter.DispatchedMessages.Count);
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
