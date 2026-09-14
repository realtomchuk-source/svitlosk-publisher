using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Graphics;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Dedicated graphic publisher dispatcher for Telegram per GRAPHIC_PUBLISHER_SPECIFICATION.
/// Manages the publication and in-place mutation (editMessageMedia) of the 12-subqueue outage schedule graphic.
/// </summary>
public class TelegramGraphicPublisherDispatcher : IGraphicPublisherDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;
    private readonly IDelayProvider _delayProvider;
    private readonly IGraphicRasterizer _rasterizer;
    private readonly TelegramRateLimiter _rateLimiter;

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
        _rateLimiter = new TelegramRateLimiter(_delayProvider);
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
            await _rateLimiter.DelayBackoffAsync(attempt, result.RetryAfterSeconds, cancellationToken).ConfigureAwait(false);
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

        string formattedDate = EditorialContentTransformer.FormatDate(payload.ScheduleDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        string caption = TelegramRenderer.FormatGraphicScheduleCaption(formattedDate);
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

        string formattedDate = EditorialContentTransformer.FormatDate(payload.ScheduleDate ?? DateTime.UtcNow.ToString("yyyy-MM-dd"));
        string caption = TelegramRenderer.FormatGraphicScheduleCaption(formattedDate);

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
                    // If JSON fails to parse but HTTP was 200 OK
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
