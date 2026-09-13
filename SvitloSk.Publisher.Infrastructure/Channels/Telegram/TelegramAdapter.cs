using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Low-level HTTP transport adapter for Telegram Bot API.
/// Coordinates with TelegramDiscussionManager for discussion operations.
/// </summary>
public class TelegramAdapter : ITelegramAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;
    private readonly TelegramDiscussionManager _discussionManager;

    public TelegramAdapter(HttpClient httpClient, string botToken, string baseUrl = "https://api.telegram.org")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException("Telegram Bot Token is missing from configuration.", nameof(botToken));
            
        _botToken = botToken;
        _baseUrl = baseUrl.TrimEnd('/');
        _discussionManager = new TelegramDiscussionManager(httpClient, botToken, _baseUrl);
    }

    public async Task<TelegramDispatchResult> SendAsync(
        string chatNameOrId,
        string text,
        byte[]? graphicBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(chatNameOrId));

        if (graphicBytes != null && graphicBytes.Length > 0)
        {
            // sendPhoto
            var url = $"{_baseUrl}/bot{_botToken}/sendPhoto";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chatNameOrId), "chat_id");
            content.Add(new ByteArrayContent(graphicBytes), "photo", "photo.png");
            content.Add(new StringContent("HTML"), "parse_mode");
            if (!string.IsNullOrEmpty(text))
            {
                content.Add(new StringContent(text), "caption");
            }
            return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // sendMessage
            var url = $"{_baseUrl}/bot{_botToken}/sendMessage";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chatNameOrId), "chat_id");
            content.Add(new StringContent(text), "text");
            content.Add(new StringContent("HTML"), "parse_mode");
            return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TelegramDispatchResult> UpdateAsync(
        string chatNameOrId,
        int messageId,
        string text,
        byte[]? graphicBytes = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(chatNameOrId));

        if (graphicBytes != null && graphicBytes.Length > 0)
        {
            // editMessageMedia
            var url = $"{_baseUrl}/bot{_botToken}/editMessageMedia";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chatNameOrId), "chat_id");
            content.Add(new StringContent(messageId.ToString()), "message_id");

            var mediaObj = new
            {
                type = "photo",
                media = "attach://photo_file",
                caption = text,
                parse_mode = "HTML"
            };
            string mediaJson = JsonSerializer.Serialize(mediaObj);
            content.Add(new StringContent(mediaJson), "media");
            content.Add(new ByteArrayContent(graphicBytes), "photo_file", "photo.png");

            return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // editMessageText
            var url = $"{_baseUrl}/bot{_botToken}/editMessageText";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(chatNameOrId), "chat_id");
            content.Add(new StringContent(messageId.ToString()), "message_id");
            content.Add(new StringContent(text), "text");
            content.Add(new StringContent("HTML"), "parse_mode");

            return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<TelegramDispatchResult> DeleteAsync(
        string chatNameOrId,
        int messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(chatNameOrId));

        var url = $"{_baseUrl}/bot{_botToken}/deleteMessage";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatNameOrId), "chat_id");
        content.Add(new StringContent(messageId.ToString()), "message_id");

        return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
    }

    public Task<TelegramDispatchResult> CloseCommentsAsync(
        string discussionGroupId,
        int channelMessageId,
        CancellationToken cancellationToken = default)
    {
        return _discussionManager.CloseCommentsAsync(discussionGroupId, channelMessageId, cancellationToken);
    }

    private async Task<TelegramDispatchResult> ExecuteRequestAsync(
        string url, 
        HttpContent content, 
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                int? msgId = null;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("result", out var resultElement))
                    {
                        if (resultElement.ValueKind == JsonValueKind.Object && resultElement.TryGetProperty("message_id", out var idElement))
                        {
                            msgId = idElement.GetInt32();
                        }
                    }
                }
                catch
                {
                    // Ignore parsing errors for success cases (e.g. deleteMessage returns boolean true)
                }

                return new TelegramDispatchResult(true, msgId, null, false);
            }

            return MapErrorCode(response.StatusCode, responseBody);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate
        }
        catch (Exception ex)
        {
            // Network failures are retryable
            return new TelegramDispatchResult(false, null, $"Network failure: {ex.Message}", true);
        }
    }

    private static TelegramDispatchResult MapErrorCode(HttpStatusCode statusCode, string responseBody)
    {
        int code = (int)statusCode;
        string description = "Unknown Telegram API Error";
        int? retryAfter = null;

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("description", out var descElement))
            {
                description = descElement.GetString() ?? description;
            }

            if (root.TryGetProperty("parameters", out var paramsElement) && 
                paramsElement.TryGetProperty("retry_after", out var retryElement))
            {
                retryAfter = retryElement.GetInt32();
            }
        }
        catch
        {
            description = $"Failed to parse API error body. Raw status: {code}";
        }

        // HTTP 429 - Rate Limit
        if (code == 429)
        {
            return new TelegramDispatchResult(false, null, description, true, retryAfter);
        }

        // HTTP 5xx - Retryable
        if (code >= 500 && code <= 599)
        {
            return new TelegramDispatchResult(false, null, description, true);
        }

        // HTTP 401 / 403 - Fatal
        if (code == 401 || code == 403 || code == 404)
        {
            return new TelegramDispatchResult(false, null, description, false);
        }

        // HTTP 400 - Permanent failure (Idempotent 400 cases for deletes and edits are resolved safely)
        if (code == 400 && (description.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) || 
                            description.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase) ||
                            description.Contains("message to delete not found", StringComparison.OrdinalIgnoreCase)))
        {
            return new TelegramDispatchResult(true, null, description, false);
        }

        return new TelegramDispatchResult(false, null, description, false);
    }
}

public class SvgSkiaRasterizer : IGraphicRasterizer
{
    public byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 1000, int height = 650)
    {
        if (svgBytes == null || svgBytes.Length == 0)
            throw new ArgumentException("SVG byte payload cannot be null or empty.", nameof(svgBytes));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        using var ms = new MemoryStream(svgBytes);
        using var svg = new Svg.Skia.SKSvg();

        try
        {
            var picture = svg.Load(ms);
            if (picture == null || svg.Picture == null)
            {
                throw new InvalidOperationException("Failed to load and parse SVG picture data.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Malformed or unparseable SVG content: {ex.Message}", ex);
        }

        var imageInfo = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var surface = SkiaSharp.SKSurface.Create(imageInfo);
        if (surface == null)
        {
            throw new InvalidOperationException($"Failed to allocate SKSurface of dimensions {width}x{height}.");
        }

        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        float scaleX = width / svg.Picture.CullRect.Width;
        float scaleY = height / svg.Picture.CullRect.Height;
        var matrix = SkiaSharp.SKMatrix.CreateScale(scaleX, scaleY);

        canvas.DrawPicture(svg.Picture, ref matrix);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        if (data == null)
        {
            throw new InvalidOperationException("Failed to encode SkiaSharp image into PNG byte stream.");
        }

        return data.ToArray();
    }
}
