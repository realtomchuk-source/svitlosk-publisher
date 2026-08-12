using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration.Channels.Telegram;

public class TelegramAdapterTests
{
    private class FakeHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>>? HandlerFunc { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, 
            CancellationToken cancellationToken)
        {
            if (HandlerFunc == null)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":12345}}", Encoding.UTF8, "application/json")
                };
            }
            return await HandlerFunc(request);
        }
    }

    [Fact]
    public async Task D03_T01_SuccessfulSendMessage_ShouldReturnSuccessWithMessageId()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        var result = await adapter.SendAsync("-1004394558011", "Test Text");

        Assert.True(result.IsSuccess);
        Assert.Equal(12345, result.MessageId);
        Assert.Null(result.ErrorDescription);
    }

    [Fact]
    public async Task D03_T02_SuccessfulSendPhoto_ShouldSubmitMultipartFormCorrectly()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/botfake-token/sendPhoto", req.RequestUri?.PathAndQuery);
                Assert.IsType<MultipartFormDataContent>(req.Content);
                
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":54321}}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        var result = await adapter.SendAsync("-1004394558011", "Caption", new byte[] { 1, 2, 3 });

        Assert.True(result.IsSuccess);
        Assert.Equal(54321, result.MessageId);
    }

    [Fact]
    public async Task D03_T06_Http429TooManyRequests_ShouldReturnRetryableWithDelay()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"ok\":false,\"description\":\"Too Many Requests\",\"parameters\":{\"retry_after\":15}}", Encoding.UTF8, "application/json")
            })
        };

        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        var result = await adapter.SendAsync("-1004394558011", "Text");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable);
        Assert.Equal(15, result.RetryAfterSeconds);
        Assert.Equal("Too Many Requests", result.ErrorDescription);
    }

    [Fact]
    public async Task D03_T09_Http401Unauthorized_ShouldReturnFatalNonRetryable()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("{\"ok\":false,\"description\":\"Unauthorized\"}", Encoding.UTF8, "application/json")
            })
        };

        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        var result = await adapter.SendAsync("-1004394558011", "Text");

        Assert.False(result.IsSuccess);
        Assert.False(result.IsRetryable);
        Assert.Equal("Unauthorized", result.ErrorDescription);
    }

    [Fact]
    public async Task D03_T13_CancellationRequested_ShouldPropagateException()
    {
        var handler = new FakeHttpMessageHandler();
        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => adapter.SendAsync("-1004394558011", "Text", null, cts.Token));
    }

    [Fact]
    public void Constructor_MissingBotToken_ShouldThrowArgumentException()
    {
        using var client = new HttpClient();
        Assert.Throws<ArgumentException>(() => new TelegramAdapter(client, ""));
    }
}
