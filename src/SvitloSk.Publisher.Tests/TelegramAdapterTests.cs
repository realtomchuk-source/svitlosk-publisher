using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Channels;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class TelegramAdapterTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastContent { get; private set; }
        public HttpResponseMessage ResponseToReturn { get; set; } = new HttpResponseMessage(HttpStatusCode.OK);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastContent = await request.Content.ReadAsStringAsync(cancellationToken);
            }
            return ResponseToReturn;
        }
    }

    [Fact]
    public async Task PublishAsync_CreateText_CallsSendMessageAndReturnsIdentity()
    {
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.ResponseToReturn.Content = new StringContent(@"{""ok"":true,""result"":{""message_id"":12345,""chat"":{""id"":-100123456789}}}");
        
        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new TelegramOptions { BotToken = "test_token", TargetChatId = "-100123456789" });
        var adapter = new TelegramAdapter(httpClient, options, NullLogger<TelegramAdapter>.Instance);

        var artifact = new TransportArtifact("req1", TransportArtifactType.TEXT_ONLY, TransportOperation.CREATE, "Test payload");

        var result = await adapter.PublishAsync(artifact);

        Assert.NotNull(mockHandler.LastRequest);
        Assert.Equal("https://api.telegram.org/bottest_token/sendMessage", mockHandler.LastRequest.RequestUri?.ToString());
        Assert.Contains(@"""chat_id"":""-100123456789""", mockHandler.LastContent);
        Assert.Contains(@"""text"":""Test payload""", mockHandler.LastContent);
        
        Assert.Equal("-100123456789:12345", result.MessageId);
        Assert.Equal("-100123456789", result.ChannelId);
    }

    [Fact]
    public async Task PublishAsync_Update_ThrowsInvalidOperationException()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new TelegramOptions { BotToken = "test_token", TargetChatId = "-100123456789" });
        var adapter = new TelegramAdapter(httpClient, options, NullLogger<TelegramAdapter>.Instance);

        var artifact = new TransportArtifact("req3", TransportArtifactType.TEXT_ONLY, TransportOperation.UPDATE, "Test payload");

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PublishAsync(artifact));
    }

    [Fact]
    public async Task PublishAsync_Delete_ThrowsInvalidOperationException()
    {
        var mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new TelegramOptions { BotToken = "test_token", TargetChatId = "-100123456789" });
        var adapter = new TelegramAdapter(httpClient, options, NullLogger<TelegramAdapter>.Instance);

        var artifact = new TransportArtifact("req4", TransportArtifactType.TEXT_ONLY, TransportOperation.DELETE, "");

        await Assert.ThrowsAsync<InvalidOperationException>(() => adapter.PublishAsync(artifact));
    }
}
