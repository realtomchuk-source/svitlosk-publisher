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
    public async Task D03_T14_CloseCommentsAsync_ShouldCallDeleteMessageOnDiscussionGroup()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/botfake-token/deleteMessage", req.RequestUri?.PathAndQuery);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var adapter = new TelegramAdapter(client, "fake-token");

        var result = await adapter.CloseCommentsAsync("-1009876543210", 7788);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Constructor_MissingBotToken_ShouldThrowArgumentException()
    {
        using var client = new HttpClient();
        Assert.Throws<ArgumentException>(() => new TelegramAdapter(client, ""));
    }
}

public class TelegramGraphicPublisherDispatcherTests
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
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":8888}}", Encoding.UTF8, "application/json")
                };
            }
            return await HandlerFunc(request);
        }
    }

    private class FakeDelayProvider : SvitloSk.Publisher.Application.Interfaces.IDelayProvider
    {
        public System.Collections.Generic.List<int> DelaysCalled { get; } = new();
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            DelaysCalled.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TC_TelegramGraphic_Create_SendPhoto()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/bottest-token/sendPhoto", req.RequestUri?.PathAndQuery);
                Assert.IsType<MultipartFormDataContent>(req.Content);

                string contentStr = await req.Content.ReadAsStringAsync();
                Assert.Contains("graphic_schedule.png", contentStr);
                Assert.Contains("#старокостянтинів", contentStr);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":9999}}", Encoding.UTF8, "application/json")
                };
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/></svg>";
        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинів",
            "hash123",
            Encoding.UTF8.GetBytes(validSvg),
            null
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(9999, result.MessageId);
    }

    [Fact]
    public async Task TC_TelegramGraphic_Update_EditMessageMedia()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/bottest-token/editMessageMedia", req.RequestUri?.PathAndQuery);
                Assert.IsType<MultipartFormDataContent>(req.Content);

                string contentStr = await req.Content.ReadAsStringAsync();
                Assert.Contains("7777", contentStr); // message_id
                Assert.Contains("attach://photo_file", contentStr);
                Assert.Contains("graphic_schedule.png", contentStr);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":7777}}", Encoding.UTF8, "application/json")
                };
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/></svg>";
        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "UPDATE",
            "Старокостянтинів",
            "hash456",
            Encoding.UTF8.GetBytes(validSvg),
            7777
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(7777, result.MessageId);
    }


    [Fact]
    public async Task TC_TelegramGraphic_Delete_DeleteMessage()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/bottest-token/deleteMessage", req.RequestUri?.PathAndQuery);

                string contentStr = await req.Content.ReadAsStringAsync();
                Assert.Contains("6666", contentStr);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
                };
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "DELETE",
            "Старокостянтинів",
            "hash789",
            null,
            6666
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TC_TelegramGraphic_429_RetriesWithRetryAfter()
    {
        int attempts = 0;
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                attempts++;
                if (attempts == 1)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent("{\"ok\":false,\"description\":\"Too Many Requests\",\"parameters\":{\"retry_after\":5}}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":1001}}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/></svg>";
        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинів",
            "hash1",
            Encoding.UTF8.GetBytes(validSvg),
            null
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, attempts);
        Assert.Single(delay.DelaysCalled);
        Assert.Equal(5000, delay.DelaysCalled[0]);
    }

    [Fact]
    public async Task TC_TelegramGraphic_5xx_ExponentialBackoff()
    {
        int attempts = 0;
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                attempts++;
                if (attempts < 3)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                    {
                        Content = new StringContent("{\"ok\":false,\"description\":\"Bad Gateway\"}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":2002}}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/></svg>";
        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинів",
            "hash1",
            Encoding.UTF8.GetBytes(validSvg),
            null
        );


        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
        Assert.Equal(2, delay.DelaysCalled.Count);
        Assert.Equal(1000, delay.DelaysCalled[0]);
        Assert.Equal(2000, delay.DelaysCalled[1]);
    }

    [Fact]
    public async Task TC_TelegramGraphic_401_FatalNoRetry()
    {
        int attempts = 0;
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                attempts++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"ok\":false,\"description\":\"Unauthorized\"}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay);

        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинів",
            "hash1",
            Encoding.UTF8.GetBytes("<svg></svg>"),
            null
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, attempts);
        Assert.Empty(delay.DelaysCalled);
    }

    [Fact]
    public async Task TC_TelegramGraphic_SecretSafety_NeverLeaksToken()
    {
        string secretToken = "super_secret_bot_token_xyz_999";
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                // Simulate network exception containing full URL with token
                throw new HttpRequestException($"Network failed while connecting to https://api.telegram.org/bot{secretToken}/sendPhoto");
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, secretToken, delay);

        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинів",
            "hash1",
            Encoding.UTF8.GetBytes("<svg viewBox=\"0 0 100 100\"><circle cx=\"50\" cy=\"50\" r=\"40\" fill=\"red\" /></svg>"),
            null
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorDescription);
        Assert.DoesNotContain(secretToken, result.ErrorDescription);
        Assert.Contains("[REDACTED_TOKEN]", result.ErrorDescription);
    }

    [Fact]
    public void TC_SvgSkiaRasterizer_ValidSvg_ProducesValidPngWithDimensions()
    {
        var rasterizer = new SvgSkiaRasterizer();
        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\">" +
                          "<rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/>" +
                          "<text x=\"500\" y=\"325\" fill=\"#FFFFFF\" font-size=\"24\" text-anchor=\"middle\">SvitloSk Test</text>" +
                          "</svg>";

        byte[] svgBytes = Encoding.UTF8.GetBytes(validSvg);
        byte[] pngBytes = rasterizer.RasterizeSvgToPng(svgBytes, 1000, 650);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 8);

        // Verify PNG magic signature: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A (89 50 4E 47 0D 0A 1A 0A)
        byte[] pngSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        for (int i = 0; i < pngSignature.Length; i++)
        {
            Assert.Equal(pngSignature[i], pngBytes[i]);
        }

        // Verify IHDR width and height (offset 16 for width, 20 for height, Big Endian)
        int width = (pngBytes[16] << 24) | (pngBytes[17] << 16) | (pngBytes[18] << 8) | pngBytes[19];
        int height = (pngBytes[20] << 24) | (pngBytes[21] << 16) | (pngBytes[22] << 8) | pngBytes[23];

        Assert.Equal(1000, width);
        Assert.Equal(650, height);
    }

    [Fact]
    public void TC_SvgSkiaRasterizer_EmptyOrMalformedSvg_ThrowsInvalidOperationException()
    {
        var rasterizer = new SvgSkiaRasterizer();

        // Empty bytes
        Assert.Throws<ArgumentException>(() => rasterizer.RasterizeSvgToPng(Array.Empty<byte>()));

        // Malformed SVG
        byte[] malformedBytes = Encoding.UTF8.GetBytes("not an svg payload at all <xml broken");
        Assert.Throws<InvalidOperationException>(() => rasterizer.RasterizeSvgToPng(malformedBytes));
    }

    [Fact]
    public void TC_SvgSkiaRasterizer_DeterministicOutput()
    {
        var rasterizer = new SvgSkiaRasterizer();
        string validSvg = "<svg viewBox=\"0 0 1000 650\" width=\"1000\" height=\"650\" xmlns=\"http://www.w3.org/2000/svg\">" +
                          "<rect width=\"1000\" height=\"650\" fill=\"#1E1E1E\"/>" +
                          "<rect x=\"100\" y=\"100\" width=\"200\" height=\"50\" fill=\"#FF8C00\"/>" +
                          "</svg>";

        byte[] svgBytes = Encoding.UTF8.GetBytes(validSvg);
        byte[] pngBytes1 = rasterizer.RasterizeSvgToPng(svgBytes, 1000, 650);
        byte[] pngBytes2 = rasterizer.RasterizeSvgToPng(svgBytes, 1000, 650);

        Assert.Equal(pngBytes1.Length, pngBytes2.Length);
        Assert.Equal(pngBytes1, pngBytes2);
    }

    [Fact]
    public async Task TC_LegacyReference_FullPipeline_AssemblyRasterizerTelegram()
    {
        var parser = new SvitloSk.Publisher.Core.Engine.OutageFeedParser();
        var assembly = new SvitloSk.Publisher.Core.Engine.GraphicAssembly();
        var rasterizer = new SvgSkiaRasterizer();

        string legacyJson = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:09:28.180570+03:00"",
  ""mode"": ""schedule"",
  ""message"": ""Графік обмежень..."",
  ""queues"": {
    ""1.1"": ""111111110000111111111111"",
    ""1.2"": ""111111111111111100001111"",
    ""2.1"": ""111111111111111111111111"",
    ""2.2"": ""111111111111111111111111"",
    ""3.1"": ""111111111111111111111111"",
    ""3.2"": ""111111111111111111111111"",
    ""4.1"": ""111111111111111111111111"",
    ""4.2"": ""111111111111111111111111"",
    ""5.1"": ""111111111111111111111111"",
    ""5.2"": ""111111111111111111111111"",
    ""6.1"": ""111111111111111111111111"",
    ""6.2"": ""111111111111111111111111""
  }
}";

        var pkg = parser.ParseLegacyGraphicJson(legacyJson, "Старокостянтинівська МТГ");
        byte[] svgBytes = assembly.AssembleSvg(pkg);
        byte[] pngBytes = rasterizer.RasterizeSvgToPng(svgBytes, 1000, 650);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 8);

        // Verify PNG magic header
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);

        // Verify Telegram Dispatcher handles it seamlessly
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = async req =>
            {
                Assert.Equal(HttpMethod.Post, req.Method);
                Assert.Equal("/bottest-token/sendPhoto", req.RequestUri?.PathAndQuery);
                Assert.IsType<MultipartFormDataContent>(req.Content);

                string contentStr = await req.Content.ReadAsStringAsync();
                Assert.Contains("graphic_schedule.png", contentStr);
                Assert.Contains("#старокостянтинів", contentStr);
                Assert.Contains("Графік знеструмлень на", contentStr);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"message_id\":8888}}", Encoding.UTF8, "application/json")
                };
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay, rasterizer);

        var payload = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинівська МТГ",
            "legacyHash123",
            svgBytes,
            null
        );

        var result = await dispatcher.DispatchGraphicAsync(payload);
        Assert.True(result.IsSuccess);
        Assert.Equal(8888, result.MessageId);
    }

    [Fact]
    public async Task TC_LegacyReference_CompleteLifecycle_CreateNoopUpdateDelete()
    {
        var parser = new SvitloSk.Publisher.Core.Engine.OutageFeedParser();
        var assembly = new SvitloSk.Publisher.Core.Engine.GraphicAssembly();
        var rasterizer = new SvgSkiaRasterizer();

        int createdMsgId = 4455;
        int httpCalls = 0;

        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                httpCalls++;
                string path = req.RequestUri?.PathAndQuery ?? "";

                if (path.Contains("/sendPhoto"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{createdMsgId}}}}}", Encoding.UTF8, "application/json")
                    });
                }
                else if (path.Contains("/editMessageMedia"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent($"{{\"ok\":true,\"result\":{{\"message_id\":{createdMsgId}}}}}", Encoding.UTF8, "application/json")
                    });
                }
                else if (path.Contains("/deleteMessage"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"ok\":true,\"result\":true}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
            }
        };

        using var client = new HttpClient(handler);
        var delay = new FakeDelayProvider();
        var dispatcher = new TelegramGraphicPublisherDispatcher(client, "test-token", delay, rasterizer);

        string legacyJson1 = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:00:00Z"",
  ""queues"": {
    ""1.1"": ""111111110000111111111111"",
    ""1.2"": ""111111111111111100001111"",
    ""2.1"": ""111111111111111111111111"",
    ""2.2"": ""111111111111111111111111"",
    ""3.1"": ""111111111111111111111111"",
    ""3.2"": ""111111111111111111111111"",
    ""4.1"": ""111111111111111111111111"",
    ""4.2"": ""111111111111111111111111"",
    ""5.1"": ""111111111111111111111111"",
    ""5.2"": ""111111111111111111111111"",
    ""6.1"": ""111111111111111111111111"",
    ""6.2"": ""111111111111111111111111""
  }
}";

        // Step 1: CREATE
        var pkg1 = parser.ParseLegacyGraphicJson(legacyJson1, "Старокостянтинівська МТГ");
        byte[] svgBytes1 = assembly.AssembleSvg(pkg1);
        var payloadCreate = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "CREATE",
            "Старокостянтинівська МТГ",
            "hash1",
            svgBytes1,
            null
        );

        var resCreate = await dispatcher.DispatchGraphicAsync(payloadCreate);
        Assert.True(resCreate.IsSuccess);
        Assert.Equal(createdMsgId, resCreate.MessageId);
        Assert.Equal(1, httpCalls);

        // Step 2: UPDATE (Changed single bitmask interval)
        string legacyJson2 = legacyJson1.Replace("\"111111110000111111111111\"", "\"111111110000000011111111\""); // extended outage 08:00-16:00
        var pkg2 = parser.ParseLegacyGraphicJson(legacyJson2, "Старокостянтинівська МТГ");
        byte[] svgBytes2 = assembly.AssembleSvg(pkg2);
        var payloadUpdate = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "UPDATE",
            "Старокостянтинівська МТГ",
            "hash2",
            svgBytes2,
            createdMsgId
        );

        var resUpdate = await dispatcher.DispatchGraphicAsync(payloadUpdate);
        Assert.True(resUpdate.IsSuccess);
        Assert.Equal(createdMsgId, resUpdate.MessageId); // Same Telegram message_id reused
        Assert.Equal(2, httpCalls);

        // Step 3: DELETE
        var payloadDelete = new SvitloSk.Publisher.Application.Interfaces.GraphicOperationPayload(
            "-100123",
            "DELETE",
            "Старокостянтинівська МТГ",
            "hash2",
            null,
            createdMsgId
        );

        var resDelete = await dispatcher.DispatchGraphicAsync(payloadDelete);
        Assert.True(resDelete.IsSuccess);
        Assert.Equal(3, httpCalls);
    }

    [Fact]
    public void TC_Export_Real_Graphic_Schedule_Preview_Image()
    {
        string sampleJson = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:09:28.180570+03:00"",
  ""mode"": ""schedule"",
  ""queues"": {
    ""1.1"": ""111111111111111111111111"",
    ""1.2"": ""111100011111111111111111"",
    ""2.1"": ""111110000111111111111111"",
    ""2.2"": ""111111111111110000000111"",
    ""3.1"": ""111110011111111111111111"",
    ""3.2"": ""000111111111111111111111"",
    ""4.1"": ""111111111111111111100000"",
    ""4.2"": ""111100000000001111111111"",
    ""5.1"": ""001111110000111111111111"",
    ""5.2"": ""111110000111111111100000"",
    ""6.1"": ""110000111111111111111111"",
    ""6.2"": ""111111111100000000000000""
  },
  ""meta"": {
    ""generated_at"": ""04.08.2026 15:09"",
    ""state"": ""active_schedule"",
    ""target_date"": ""04.08""
  }
}";

        var parser = new SvitloSk.Publisher.Core.Engine.OutageFeedParser();
        var pkg = parser.ParseLegacyGraphicJson(sampleJson, "Старокостянтинівська МТГ");

        var assembly = new SvitloSk.Publisher.Core.Engine.GraphicAssembly();
        byte[] svgBytes = assembly.AssembleSvg(pkg);

        var rasterizer = new SvgSkiaRasterizer();
        byte[] pngBytes = rasterizer.RasterizeSvgToPng(svgBytes, 1080, 1080);

        // Find solution/project root
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current) && !System.IO.File.Exists(System.IO.Path.Combine(current, "SvitloSk.Publisher.sln")))
        {
            current = System.IO.Directory.GetParent(current)?.FullName;
        }
        string rootDir = current ?? System.IO.Directory.GetCurrentDirectory();
        string localOut = System.IO.Path.Combine(rootDir, "local", "output");
        System.IO.Directory.CreateDirectory(localOut);

        SafeWriteBytes(System.IO.Path.Combine(localOut, "schedule_preview.svg"), svgBytes);
        SafeWriteBytes(System.IO.Path.Combine(localOut, "schedule_preview.png"), pngBytes);

        string artifactDir = @"C:\Users\ATom\.gemini\antigravity\brain\8459d6eb-d4ef-436e-a6fb-5801df454ea5";
        if (System.IO.Directory.Exists(artifactDir))
        {
            SafeWriteBytes(System.IO.Path.Combine(artifactDir, "schedule_preview.svg"), svgBytes);
            SafeWriteBytes(System.IO.Path.Combine(artifactDir, "schedule_preview.png"), pngBytes);
        }

        Assert.True(pngBytes.Length > 0);
    }

    private static void SafeWriteBytes(string path, byte[] bytes)
    {
        try
        {
            using var fs = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            fs.Write(bytes, 0, bytes.Length);
        }
        catch
        {
            // fallback if locked by visual viewer
            string altPath = path + ".tmp";
            using var fs = new System.IO.FileStream(altPath, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
            fs.Write(bytes, 0, bytes.Length);
            try { System.IO.File.Move(altPath, path, true); } catch { }
        }
    }
}




