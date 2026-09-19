using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.WhatsApp;

public class WhatsAppBridgeAdapterTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Handler { get; set; } =
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Handler(request);
        }
    }

    [Fact]
    public async Task SendTextMessageAsync_SuccessfulResponse_ReturnsSuccessAndMessageId()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://127.0.0.1:3000/send", req.RequestUri?.ToString());
                string body = await req.Content!.ReadAsStringAsync();
                Assert.Contains("\"channelOrChatId\":\"120363@newsletter\"", body);
                Assert.Contains("\"text\":\"Test Message\"", body);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"isSuccess\":true,\"messageId\":\"msg_12345\"}", Encoding.UTF8, "application/json")
                };
            }
        };

        var client = new HttpClient(handler);
        var adapter = new WhatsAppBridgeAdapter(client, "http://127.0.0.1:3000");

        var result = await adapter.SendTextMessageAsync("120363@newsletter", "Test Message");

        Assert.True(result.IsSuccess);
        Assert.Equal("msg_12345", result.MessageId);
    }

    [Fact]
    public async Task SendMediaMessageAsync_WithBytes_SendsBase64AndReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://127.0.0.1:3000/media", req.RequestUri?.ToString());
                string body = await req.Content!.ReadAsStringAsync();
                Assert.Contains("\"imageBase64\"", body);
                Assert.Contains("\"caption\":\"Media Caption\"", body);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"isSuccess\":true,\"messageId\":\"media_999\"}", Encoding.UTF8, "application/json")
                };
            }
        };

        var client = new HttpClient(handler);
        var adapter = new WhatsAppBridgeAdapter(client, "http://127.0.0.1:3000");

        byte[] fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var result = await adapter.SendMediaMessageAsync("120363@newsletter", "Media Caption", fakePng);

        Assert.True(result.IsSuccess);
        Assert.Equal("media_999", result.MessageId);
    }

    [Fact]
    public async Task DeleteMessageAsync_SuccessfulResponse_ReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("http://127.0.0.1:3000/delete", req.RequestUri?.ToString());
                string body = await req.Content!.ReadAsStringAsync();
                Assert.Contains("\"messageId\":\"msg_delete_1\"", body);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"isSuccess\":true,\"messageId\":\"msg_delete_1\"}", Encoding.UTF8, "application/json")
                };
            }
        };

        var client = new HttpClient(handler);
        var adapter = new WhatsAppBridgeAdapter(client, "http://127.0.0.1:3000");

        var result = await adapter.DeleteMessageAsync("120363@newsletter", "msg_delete_1");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CheckChannelAccessAsync_WhenBridgeHealthyAndChannelValid_ReturnsSuccess()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                if (req.RequestUri!.AbsolutePath.EndsWith("/health"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"status\":\"ok\",\"connected\":true}", Encoding.UTF8, "application/json")
                    });
                }
                if (req.RequestUri.AbsolutePath.EndsWith("/channel-info"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"isSuccess\":true,\"name\":\"SvitloSk Channel\",\"jid\":\"120363@newsletter\"}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var client = new HttpClient(handler);
        var adapter = new WhatsAppBridgeAdapter(client, "http://127.0.0.1:3000");

        var result = await adapter.CheckChannelAccessAsync("120363@newsletter");

        Assert.True(result.IsSuccess);
        Assert.Contains("SvitloSk Channel", result.ErrorDescription);
    }

    [Fact]
    public async Task CheckChannelAccessAsync_WhenNotConnected_ReturnsFailureWithQrInstruction()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\",\"connected\":false,\"hasQr\":true}", Encoding.UTF8, "application/json")
                });
            }
        };

        var client = new HttpClient(handler);
        var adapter = new WhatsAppBridgeAdapter(client, "http://127.0.0.1:3000");

        var result = await adapter.CheckChannelAccessAsync("120363@newsletter");

        Assert.False(result.IsSuccess);
        Assert.Contains("scan the QR code", result.ErrorDescription);
    }
}
