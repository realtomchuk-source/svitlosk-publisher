using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

/// <summary>
/// Production HTTP client adapter for the Meta Graph API (v19.0).
/// Implements IFacebookAdapter to publish text posts, upload photo posts,
/// update existing post text, and delete outdated posts.
/// </summary>
public class FacebookGraphApiClient : IFacebookAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _pageAccessToken;
    private readonly string _apiVersion;

    public FacebookGraphApiClient(
        HttpClient httpClient,
        string pageAccessToken,
        string apiVersion = "v19.0")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _pageAccessToken = !string.IsNullOrWhiteSpace(pageAccessToken) 
            ? pageAccessToken 
            : throw new ArgumentException("Facebook Page Access Token cannot be null or empty.", nameof(pageAccessToken));
        _apiVersion = !string.IsNullOrWhiteSpace(apiVersion) ? apiVersion : "v19.0";
    }

    public async Task<FacebookDispatchResult> PublishPostAsync(
        string pageId,
        string text,
        byte[]? imageBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));

        try
        {
            if (imageBytes != null && imageBytes.Length > 0)
            {
                // 1. Upload photo asset as unpublished to /{pageId}/photos
                string photoUrl = $"https://graph.facebook.com/{_apiVersion}/{pageId}/photos";

                using var photoContent = new MultipartFormDataContent();
                photoContent.Add(new StringContent("false"), "published");
                photoContent.Add(new StringContent(_pageAccessToken), "access_token");

                var imageContent = new ByteArrayContent(imageBytes);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                photoContent.Add(imageContent, "source", "post_image.png");

                using var photoResponse = await _httpClient.PostAsync(photoUrl, photoContent, cancellationToken).ConfigureAwait(false);
                string photoJson = await photoResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!photoResponse.IsSuccessStatusCode)
                {
                    return ParseError(photoJson);
                }

                using var photoDoc = JsonDocument.Parse(photoJson);
                string? photoId = photoDoc.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;

                if (string.IsNullOrEmpty(photoId))
                {
                    return new FacebookDispatchResult(false, ErrorDescription: "Failed to obtain photo ID from unpublished upload.");
                }

                // 2. Publish guaranteed feed post to /{pageId}/feed with attached_media
                string feedUrl = $"https://graph.facebook.com/{_apiVersion}/{pageId}/feed";
                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("message", text),
                    new("attached_media[0]", $"{{\"media_fbid\":\"{photoId}\"}}"),
                    new("access_token", _pageAccessToken)
                };

                using var feedContent = new FormUrlEncodedContent(formValues);
                using var feedResponse = await _httpClient.PostAsync(feedUrl, feedContent, cancellationToken).ConfigureAwait(false);
                string feedJson = await feedResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return ParseResponse(feedResponse, feedJson, isCreate: true);
            }
            else
            {
                // Publish text post to /{pageId}/feed
                string url = $"https://graph.facebook.com/{_apiVersion}/{pageId}/feed";

                var formValues = new List<KeyValuePair<string, string>>
                {
                    new("message", text),
                    new("access_token", _pageAccessToken)
                };

                using var content = new FormUrlEncodedContent(formValues);
                using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
                string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                return ParseResponse(response, responseJson, isCreate: true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FacebookDispatchResult(
                IsSuccess: false,
                ErrorDescription: $"[FacebookGraphApi] Network exception: {ex.Message}",
                IsRetryable: true
            );
        }
    }

    public async Task<FacebookDispatchResult> UpdatePostAsync(
        string postId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(postId))
            throw new ArgumentException("Post ID cannot be null or empty.", nameof(postId));

        try
        {
            string url = $"https://graph.facebook.com/{_apiVersion}/{postId}";

            var formValues = new List<KeyValuePair<string, string>>
            {
                new("message", text),
                new("access_token", _pageAccessToken)
            };

            using var content = new FormUrlEncodedContent(formValues);
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return ParseResponse(response, responseJson, isCreate: false, targetPostId: postId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FacebookDispatchResult(
                IsSuccess: false,
                ErrorDescription: $"[FacebookGraphApi] Network exception on update: {ex.Message}",
                IsRetryable: true
            );
        }
    }

    public async Task<FacebookDispatchResult> DeletePostAsync(
        string postId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(postId))
            throw new ArgumentException("Post ID cannot be null or empty.", nameof(postId));

        try
        {
            string url = $"https://graph.facebook.com/{_apiVersion}/{postId}?access_token={Uri.EscapeDataString(_pageAccessToken)}";

            using var response = await _httpClient.DeleteAsync(url, cancellationToken).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return ParseResponse(response, responseJson, isCreate: false, isDelete: true, targetPostId: postId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new FacebookDispatchResult(
                IsSuccess: false,
                ErrorDescription: $"[FacebookGraphApi] Network exception on delete: {ex.Message}",
                IsRetryable: true
            );
        }
    }

    public async Task<IReadOnlyList<FacebookPostSummary>> GetRecentPostsAsync(
        string pageId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
            throw new ArgumentException("Page ID cannot be null or empty.", nameof(pageId));

        var posts = new List<FacebookPostSummary>();
        try
        {
            string url = $"https://graph.facebook.com/{_apiVersion}/{pageId}/posts?fields=id,message,created_time&limit={limit}&access_token={Uri.EscapeDataString(_pageAccessToken)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return posts;
            }

            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("data", out var dataArray) && dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    string? id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrWhiteSpace(id)) continue;

                    string? message = item.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : null;
                    DateTimeOffset? createdTime = null;
                    if (item.TryGetProperty("created_time", out var timeProp) && DateTimeOffset.TryParse(timeProp.GetString(), out var parsedTime))
                    {
                        createdTime = parsedTime;
                    }

                    posts.Add(new FacebookPostSummary(id, message, createdTime));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING][FacebookGraphApiClient] GetRecentPostsAsync failed gracefully: {ex.Message}");
        }

        return posts;
    }

    public async Task<FacebookDispatchResult> CheckPageAccessAsync(
        string pageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"https://graph.facebook.com/{_apiVersion}/{pageId}?fields=id,name&access_token={Uri.EscapeDataString(_pageAccessToken)}";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseJson);
                string name = doc.RootElement.TryGetProperty("name", out var n) ? n.GetString() ?? pageId : pageId;
                return new FacebookDispatchResult(true, PostId: pageId, ErrorDescription: $"Connected to Page '{name}' (ID: {pageId})");
            }
            else
            {
                return ParseError(responseJson);
            }
        }
        catch (Exception ex)
        {
            return new FacebookDispatchResult(false, ErrorDescription: $"CheckPageAccess failed: {ex.Message}");
        }
    }

    private static FacebookDispatchResult ParseResponse(
        HttpResponseMessage response,
        string responseJson,
        bool isCreate,
        bool isDelete = false,
        string? targetPostId = null)
    {
        if (response.IsSuccessStatusCode)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (isCreate)
                {
                    // Photo upload might return { "id": "photo_id", "post_id": "page_post" }
                    string? postId = null;
                    if (root.TryGetProperty("post_id", out var pId) && pId.GetString() != null)
                    {
                        postId = pId.GetString();
                    }
                    else if (root.TryGetProperty("id", out var id))
                    {
                        postId = id.GetString();
                    }

                    return new FacebookDispatchResult(
                        IsSuccess: true,
                        PostId: postId
                    );
                }
                else
                {
                    bool success = true;
                    if (root.TryGetProperty("success", out var s))
                    {
                        success = s.GetBoolean();
                    }

                    return new FacebookDispatchResult(
                        IsSuccess: success,
                        PostId: targetPostId
                    );
                }
            }
            catch (Exception ex)
            {
                return new FacebookDispatchResult(
                    IsSuccess: false,
                    ErrorDescription: $"Failed to parse Facebook API response: {ex.Message}. Response: {responseJson}"
                );
            }
        }

        return ParseError(responseJson, isDelete, targetPostId);
    }

    private static FacebookDispatchResult ParseError(string responseJson, bool isDelete = false, string? targetPostId = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                string message = err.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
                int code = err.TryGetProperty("code", out var c) ? c.GetInt32() : 0;
                int subcode = err.TryGetProperty("error_subcode", out var sc) ? sc.GetInt32() : 0;

                // Idempotent delete: if object doesn't exist or cannot be loaded, it is already deleted
                if (isDelete && (code is 100 or 803 or 200) && (subcode == 33 || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) || message.Contains("Unsupported delete request", StringComparison.OrdinalIgnoreCase) || message.Contains("cannot be loaded", StringComparison.OrdinalIgnoreCase)))
                {
                    return new FacebookDispatchResult(
                        IsSuccess: true,
                        PostId: targetPostId
                    );
                }

                // Transient / rate-limit codes in Meta Graph API: 4, 17, 32, 341, 613
                bool isRetryable = code is 4 or 17 or 32 or 341 or 613;

                return new FacebookDispatchResult(
                    IsSuccess: false,
                    ErrorDescription: $"[Facebook Error {code}:{subcode}] {message}",
                    IsRetryable: isRetryable
                );
            }
        }
        catch { }

        return new FacebookDispatchResult(
            IsSuccess: false,
            ErrorDescription: $"[Facebook API Error] Raw: {responseJson}"
        );
    }
}
