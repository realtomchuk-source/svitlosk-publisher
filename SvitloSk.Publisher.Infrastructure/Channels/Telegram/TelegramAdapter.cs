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

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;


public class TelegramAdapter : ITelegramAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;

    public TelegramAdapter(HttpClient httpClient, string botToken, string baseUrl = "https://api.telegram.org")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        
        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException("Telegram Bot Token is missing from configuration.", nameof(botToken));
            
        _botToken = botToken;
        _baseUrl = baseUrl.TrimEnd('/');
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
            // If the message text is not modified, or message is not found on deletion/editing, we map it as success to keep idempotency transitions stable
            return new TelegramDispatchResult(true, null, description, false);
        }


        return new TelegramDispatchResult(false, null, description, false);
    }
}

public interface IGraphicRasterizer
{
    byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 1000, int height = 650);
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

public class TelegramGraphicPublisherDispatcher : IGraphicPublisherDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;
    private readonly IDelayProvider _delayProvider;
    private readonly IGraphicRasterizer _rasterizer;

    public TelegramGraphicPublisherDispatcher(
        HttpClient httpClient,
        string botToken,
        IDelayProvider? delayProvider = null,
        IGraphicRasterizer? rasterizer = null,
        string baseUrl = "https://api.telegram.org")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

        if (string.IsNullOrWhiteSpace(botToken))
            throw new ArgumentException("Telegram Bot Token is missing from configuration.", nameof(botToken));

        _botToken = botToken;
        _baseUrl = baseUrl.TrimEnd('/');
        _delayProvider = delayProvider ?? new SystemDelayProvider();
        _rasterizer = rasterizer ?? new SvgSkiaRasterizer();
    }

    public async Task<TelegramDispatchResult> DispatchGraphicAsync(
        GraphicOperationPayload payload,
        CancellationToken cancellationToken = default)
    {
        if (payload == null)
            throw new ArgumentNullException(nameof(payload));

        if (string.IsNullOrWhiteSpace(payload.ChatNameOrId))
            throw new ArgumentException("Chat identifier cannot be null or empty.", nameof(payload));

        int attempt = 0;
        const int maxAttempts = 3;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            TelegramDispatchResult result;

            switch (payload.OperationType.ToUpperInvariant())
            {
                case "CREATE":
                    result = await SendPhotoAsync(payload, cancellationToken).ConfigureAwait(false);
                    break;

                case "UPDATE":
                    if (!payload.TelegramMessageId.HasValue)
                        throw new InvalidOperationException("Cannot update graphic publication without its Telegram message ID.");
                    result = await EditMessageMediaAsync(payload, cancellationToken).ConfigureAwait(false);
                    break;

                case "DELETE":
                    if (!payload.TelegramMessageId.HasValue)
                        throw new InvalidOperationException("Cannot delete graphic publication without its Telegram message ID.");
                    result = await DeleteMessageAsync(payload, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported Graphic operation type: '{payload.OperationType}'.");
            }

            if (result.IsSuccess || !result.IsRetryable || attempt >= maxAttempts)
            {
                return result;
            }

            // Exponential Backoff / 429 RetryAfter
            int delayMs;
            if (result.RetryAfterSeconds.HasValue)
            {
                int delaySec = Math.Min(result.RetryAfterSeconds.Value, 60);
                delayMs = delaySec * 1000;
            }
            else
            {
                delayMs = attempt switch
                {
                    1 => 1000,
                    2 => 2000,
                    _ => 4000
                };
            }

            await _delayProvider.DelayAsync(delayMs, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<TelegramDispatchResult> SendPhotoAsync(
        GraphicOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/bot{_botToken}/sendPhoto";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(payload.ChatNameOrId), "chat_id");
        
        byte[] svgBytes = payload.SvgBytes ?? Array.Empty<byte>();
        // Rasterize SVG to PNG bytes (1080 x 1080 square canvas)
        byte[] pngBytes = _rasterizer.RasterizeSvgToPng(svgBytes, 1080, 1080);

        var pngContent = new ByteArrayContent(pngBytes);
        pngContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(pngContent, "photo", "graphic_schedule.png");
        content.Add(new StringContent("HTML"), "parse_mode");

        string caption = $"<b>ГРАФІК ЗНЕСТРУМЛЕНЬ</b>\n{payload.TerritoryId} (12 підчерг)\n#графік #старокостянтинів #svitlosk";
        content.Add(new StringContent(caption), "caption");

        return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TelegramDispatchResult> EditMessageMediaAsync(
        GraphicOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/bot{_botToken}/editMessageMedia";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(payload.ChatNameOrId), "chat_id");
        content.Add(new StringContent(payload.TelegramMessageId!.Value.ToString()), "message_id");

        string caption = $"<b>ГРАФІК ЗНЕСТРУМЛЕНЬ</b>\n{payload.TerritoryId} (12 підчерг)\n#графік #старокостянтинів #svitlosk";

        var mediaObj = new
        {
            type = "photo",
            media = "attach://photo_file",
            caption = caption,
            parse_mode = "HTML"
        };
        string mediaJson = JsonSerializer.Serialize(mediaObj);
        content.Add(new StringContent(mediaJson), "media");

        byte[] svgBytes = payload.SvgBytes ?? Array.Empty<byte>();
        // Rasterize SVG to PNG bytes (1080 x 1080 square canvas)
        byte[] pngBytes = _rasterizer.RasterizeSvgToPng(svgBytes, 1080, 1080);

        var pngContent = new ByteArrayContent(pngBytes);
        pngContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(pngContent, "photo_file", "graphic_schedule.png");

        return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TelegramDispatchResult> DeleteMessageAsync(
        GraphicOperationPayload payload,
        CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}/bot{_botToken}/deleteMessage";
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(payload.ChatNameOrId), "chat_id");
        content.Add(new StringContent(payload.TelegramMessageId!.Value.ToString()), "message_id");

        return await ExecuteRequestAsync(url, content, cancellationToken).ConfigureAwait(false);
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
                    // Ignore JSON parse errors for boolean results
                }

                return new TelegramDispatchResult(true, msgId, null, false);
            }

            return MapErrorCode(response.StatusCode, responseBody);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sanitize safe error description so botToken is never leaked
            return new TelegramDispatchResult(false, null, $"Network failure: {SanitizeError(ex.Message)}", true);
        }
    }

    private string SanitizeError(string error)
    {
        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(error)) return error;
        return error.Replace(_botToken, "[REDACTED_TOKEN]");
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

        // HTTP 401 / 403 / 404 - Fatal
        if (code == 401 || code == 403 || code == 404)
        {
            return new TelegramDispatchResult(false, null, description, false);
        }

        // HTTP 400 - Permanent failure (Idempotent 400 cases for deletes or unchanged media)
        if (code == 400 && (description.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) || description.Contains("message to edit not found", StringComparison.OrdinalIgnoreCase)))
        {
            return new TelegramDispatchResult(true, null, description, false);
        }

        return new TelegramDispatchResult(false, null, description, false);
    }
}


