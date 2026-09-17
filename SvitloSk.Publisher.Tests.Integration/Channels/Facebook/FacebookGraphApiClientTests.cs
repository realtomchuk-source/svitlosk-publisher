using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration.Channels.Facebook;

public class FacebookGraphApiClientTests
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
                    Content = new StringContent("{\"id\":\"page123_post456\"}", Encoding.UTF8, "application/json")
                };
            }
            return await HandlerFunc(request);
        }
    }

    [Fact]
    public async Task PublishPostAsync_TextPost_SendsToFeedEndpointAndReturnsPostId()
    {
        string? requestedUrl = null;
        string? requestBody = null;

        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = async req =>
            {
                requestedUrl = req.RequestUri?.ToString();
                requestBody = req.Content != null ? await req.Content.ReadAsStringAsync() : null;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"1001_2002\"}", Encoding.UTF8, "application/json")
                };
            }
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "test_token_abc");

        var result = await apiClient.PublishPostAsync("page_1001", "Hello Facebook!");

        Assert.True(result.IsSuccess);
        Assert.Equal("1001_2002", result.PostId);
        Assert.Contains("graph.facebook.com/v19.0/page_1001/feed", requestedUrl);
        Assert.Contains("Hello+Facebook", requestBody);
        Assert.Contains("test_token_abc", requestBody);
    }

    [Fact]
    public async Task PublishPostAsync_PhotoPost_SendsMultipartToPhotosEndpoint()
    {
        var requestedUrls = new List<string>();
        bool isMultipart = false;

        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                requestedUrls.Add(req.RequestUri?.ToString() ?? "");
                if (req.Content is MultipartFormDataContent) isMultipart = true;

                if (req.RequestUri?.ToString().Contains("/photos") == true)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"id\":\"photo_777\"}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"id\":\"1001_photo_post_888\"}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "test_token_abc");

        byte[] fakePng = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }; // PNG magic header
        var result = await apiClient.PublishPostAsync("page_1001", "Photo Caption", fakePng);

        Assert.True(result.IsSuccess);
        Assert.Equal("1001_photo_post_888", result.PostId);
        Assert.Contains(requestedUrls, u => u.Contains("graph.facebook.com/v19.0/page_1001/photos"));
        Assert.Contains(requestedUrls, u => u.Contains("graph.facebook.com/v19.0/page_1001/feed"));
        Assert.True(isMultipart);
    }

    [Fact]
    public async Task UpdatePostAsync_SendsMessageToPostEndpoint()
    {
        string? requestedUrl = null;

        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                requestedUrl = req.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "test_token_abc");

        var result = await apiClient.UpdatePostAsync("1001_2002", "Updated text");

        Assert.True(result.IsSuccess);
        Assert.Contains("graph.facebook.com/v19.0/1001_2002", requestedUrl);
    }

    [Fact]
    public async Task DeletePostAsync_SendsDeleteRequest()
    {
        HttpMethod? method = null;
        string? requestedUrl = null;

        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req =>
            {
                method = req.Method;
                requestedUrl = req.RequestUri?.ToString();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"success\":true}", Encoding.UTF8, "application/json")
                });
            }
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "test_token_abc");

        var result = await apiClient.DeletePostAsync("1001_2002");

        Assert.True(result.IsSuccess);
        Assert.Equal(HttpMethod.Delete, method);
        Assert.Contains("graph.facebook.com/v19.0/1001_2002", requestedUrl);
    }

    [Fact]
    public async Task PublishPostAsync_ErrorResponse_ParsesMetaError()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Invalid OAuth access token.\",\"type\":\"OAuthException\",\"code\":190}}", Encoding.UTF8, "application/json")
            })
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "bad_token");

        var result = await apiClient.PublishPostAsync("page_1001", "Fail");

        Assert.False(result.IsSuccess);
        Assert.Contains("190", result.ErrorDescription);
        Assert.Contains("Invalid OAuth access token", result.ErrorDescription);
        Assert.False(result.IsRetryable); // 190 is non-retryable
    }

    [Fact]
    public async Task PublishPostAsync_RateLimitError_FlagsAsRetryable()
    {
        var handler = new FakeHttpMessageHandler
        {
            HandlerFunc = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("{\"error\":{\"message\":\"User request limit reached\",\"type\":\"OAuthException\",\"code\":17}}", Encoding.UTF8, "application/json")
            })
        };

        using var client = new HttpClient(handler);
        var apiClient = new FacebookGraphApiClient(client, "rate_limited_token");

        var result = await apiClient.PublishPostAsync("page_1001", "Fail");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsRetryable); // Code 17 is retryable
    }
}
