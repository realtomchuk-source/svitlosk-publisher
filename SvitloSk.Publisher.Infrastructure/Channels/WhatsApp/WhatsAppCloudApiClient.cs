using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// Low-level HTTP transport client for WhatsApp Cloud API & Meta Graph API.
/// Target specification: Meta Graph API v20.0+ / WhatsApp Business Platform / WhatsApp Channels (Newsletter).
/// </summary>
public class WhatsAppCloudApiClient : IWhatsAppAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _baseUrl;

    public WhatsAppCloudApiClient(HttpClient httpClient, string accessToken, string baseUrl = "https://graph.facebook.com/v20.0")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new ArgumentException("WhatsApp access token cannot be null or empty.", nameof(accessToken));

        _accessToken = accessToken;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<WhatsAppDispatchResult> SendTextMessageAsync(
        string channelOrChatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel or Chat ID cannot be null or empty.", nameof(channelOrChatId));

        var url = $"{_baseUrl}/{channelOrChatId}/messages";

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = channelOrChatId,
            type = "text",
            text = new { preview_url = false, body = text }
        };

        return await SendJsonRequestAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> SendMediaMessageAsync(
        string channelOrChatId,
        string caption,
        byte[] mediaBytes,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel or Chat ID cannot be null or empty.", nameof(channelOrChatId));

        if (mediaBytes == null || mediaBytes.Length == 0)
        {
            return await SendTextMessageAsync(channelOrChatId, caption, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            // 1. Upload Media
            var uploadUrl = $"{_baseUrl}/{channelOrChatId}/media";
            using var formData = new MultipartFormDataContent();
            formData.Add(new StringContent("whatsapp"), "messaging_product");
            formData.Add(new ByteArrayContent(mediaBytes), "file", "banner.png");
            formData.Add(new StringContent(mimeType), "type");

            using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, uploadUrl);
            uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            uploadRequest.Content = formData;

            using var uploadResponse = await _httpClient.SendAsync(uploadRequest, cancellationToken).ConfigureAwait(false);
            string uploadJson = await uploadResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!uploadResponse.IsSuccessStatusCode)
            {
                return MapErrorCode(uploadResponse.StatusCode, uploadJson);
            }

            string? mediaId = null;
            using (var doc = JsonDocument.Parse(uploadJson))
            {
                if (doc.RootElement.TryGetProperty("id", out var idElem))
                {
                    mediaId = idElem.GetString();
                }
            }

            if (string.IsNullOrEmpty(mediaId))
            {
                return new WhatsAppDispatchResult(false, null, "Failed to retrieve media ID from upload response.", false);
            }

            // 2. Dispatch Media Message with caption
            var messagesUrl = $"{_baseUrl}/{channelOrChatId}/messages";
            var messagePayload = new
            {
                messaging_product = "whatsapp",
                recipient_type = "individual",
                to = channelOrChatId,
                type = "image",
                image = new
                {
                    id = mediaId,
                    caption = caption
                }
            };

            return await SendJsonRequestAsync(messagesUrl, messagePayload, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Network failure during media dispatch: {ex.Message}", true);
        }
    }

    public async Task<WhatsAppDispatchResult> UpdateTextMessageAsync(
        string channelOrChatId,
        string messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel or Chat ID cannot be null or empty.", nameof(channelOrChatId));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message ID cannot be null or empty.", nameof(messageId));

        // Meta Graph API WhatsApp message edit endpoint (within allowed edit window)
        var url = $"{_baseUrl}/{channelOrChatId}/messages";
        var payload = new
        {
            messaging_product = "whatsapp",
            status = "edited",
            message_id = messageId,
            text = new { body = text }
        };

        return await SendJsonRequestAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> DeleteMessageAsync(
        string channelOrChatId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return new WhatsAppDispatchResult(true, null, "No message ID specified for deletion", false);

        var url = $"{_baseUrl}/{messageId}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return new WhatsAppDispatchResult(true, MessageId: messageId);
            }

            // If message not found, treat as idempotent success
            if (response.StatusCode == HttpStatusCode.NotFound || responseBody.Contains("does not exist", StringComparison.OrdinalIgnoreCase))
            {
                return new WhatsAppDispatchResult(true, MessageId: messageId);
            }

            return MapErrorCode(response.StatusCode, responseBody);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Network failure during deletion: {ex.Message}", true);
        }
    }

    public async Task<WhatsAppDispatchResult> CheckChannelAccessAsync(
        string channelOrChatId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            return new WhatsAppDispatchResult(false, null, "Channel identifier is empty.", false);

        var url = $"{_baseUrl}/{channelOrChatId}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                string name = channelOrChatId;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("name", out var nElem))
                        name = nElem.GetString() ?? channelOrChatId;
                }
                catch { }

                return new WhatsAppDispatchResult(true, ErrorDescription: $"Connected to WhatsApp entity '{name}' (ID: {channelOrChatId})");
            }

            return MapErrorCode(response.StatusCode, responseBody);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Network failure during channel check: {ex.Message}", true);
        }
    }

    private async Task<WhatsAppDispatchResult> SendJsonRequestAsync(string url, object payload, CancellationToken cancellationToken)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                string? msgId = null;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("messages", out var msgs) && msgs.GetArrayLength() > 0)
                    {
                        var first = msgs[0];
                        if (first.TryGetProperty("id", out var idProp))
                        {
                            msgId = idProp.GetString();
                        }
                    }
                    else if (root.TryGetProperty("id", out var idProp))
                    {
                        msgId = idProp.GetString();
                    }
                }
                catch { }

                return new WhatsAppDispatchResult(true, MessageId: msgId);
            }

            return MapErrorCode(response.StatusCode, responseBody);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Network failure: {ex.Message}", true);
        }
    }

    private static WhatsAppDispatchResult MapErrorCode(HttpStatusCode statusCode, string responseBody)
    {
        int code = (int)statusCode;
        string description = "Unknown WhatsApp API Error";
        int? retryAfter = null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var errElement))
            {
                if (errElement.TryGetProperty("message", out var msgElem))
                {
                    description = msgElem.GetString() ?? description;
                }
            }
        }
        catch
        {
            description = $"Failed to parse API error body. Raw status: {code}";
        }

        // Rate limit (HTTP 429)
        if (code == 429)
        {
            return new WhatsAppDispatchResult(false, null, description, true, retryAfter ?? 10);
        }

        // Server errors (HTTP 5xx) - Retryable
        if (code >= 500 && code <= 599)
        {
            return new WhatsAppDispatchResult(false, null, description, true);
        }

        // Authentication/Permissions (HTTP 401, 403) - Fatal
        if (code == 401 || code == 403)
        {
            return new WhatsAppDispatchResult(false, null, description, false);
        }

        return new WhatsAppDispatchResult(false, null, description, false);
    }
}
