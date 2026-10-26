using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// HTTP adapter for local WhatsApp Bridge microservice (Playwright / Node.js).
/// Connects SvitloSk Publisher to WhatsApp Channels via authenticated local bridge.
/// </summary>
public class WhatsAppBridgeAdapter : IWhatsAppAdapter
{
    private readonly HttpClient _httpClient;
    private readonly string _bridgeBaseUrl;

    public WhatsAppBridgeAdapter(HttpClient httpClient, string bridgeBaseUrl = "http://127.0.0.1:3000")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _bridgeBaseUrl = (string.IsNullOrWhiteSpace(bridgeBaseUrl) ? "http://127.0.0.1:3000" : bridgeBaseUrl).TrimEnd('/');
    }

    public async Task<WhatsAppDispatchResult> SendTextMessageAsync(
        string channelOrChatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel identifier cannot be null or empty.", nameof(channelOrChatId));

        var url = $"{_bridgeBaseUrl}/send";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            text = text
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> SendMediaMessageAsync(
        string channelOrChatId,
        string caption,
        byte[] mediaBytes,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel identifier cannot be null or empty.", nameof(channelOrChatId));

        if (mediaBytes == null || mediaBytes.Length == 0)
        {
            return await SendTextMessageAsync(channelOrChatId, caption, cancellationToken).ConfigureAwait(false);
        }

        var url = $"{_bridgeBaseUrl}/media";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            caption = caption,
            imageBase64 = Convert.ToBase64String(mediaBytes),
            mimeType = mimeType
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> UpdateTextMessageAsync(
        string channelOrChatId,
        string messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel identifier cannot be null or empty.", nameof(channelOrChatId));
        if (string.IsNullOrWhiteSpace(messageId))
            throw new ArgumentException("Message identifier cannot be null or empty.", nameof(messageId));

        string? marker = null;
        if (WhatsAppContentFormatter.TryExtractMarkerFromText(text, out var parsedMarker) && parsedMarker != null)
        {
            marker = parsedMarker.Marker;
        }

        var url = $"{_bridgeBaseUrl}/edit";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            messageId = messageId,
            text = text,
            marker = marker
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> DeleteMessageAsync(
        string channelOrChatId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return new WhatsAppDispatchResult(true, null, "No message ID specified for deletion", false, OperationStatus: "notFound");

        var url = $"{_bridgeBaseUrl}/delete";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            messageId = messageId
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> DeleteMessageByMarkerAsync(
        string channelOrChatId,
        string? messageId,
        string? marker,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId) && string.IsNullOrWhiteSpace(marker))
            return new WhatsAppDispatchResult(true, null, "No message ID or marker specified for deletion", false, OperationStatus: "notFound");

        var url = $"{_bridgeBaseUrl}/delete";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            messageId = messageId,
            marker = marker
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppTailStateResult> GetTailStateAsync(
        string channelOrChatId,
        string? todayDdMm = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel identifier cannot be null or empty.", nameof(channelOrChatId));

        try
        {
            string query = $"channelId={Uri.EscapeDataString(channelOrChatId)}";
            if (!string.IsNullOrWhiteSpace(todayDdMm))
            {
                query += $"&todayDdMm={Uri.EscapeDataString(todayDdMm)}";
            }

            var url = $"{_bridgeBaseUrl}/tail-state?{query}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var res = await _httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
            string body = await res.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
            {
                return new WhatsAppTailStateResult(false, Array.Empty<WhatsAppTailMessage>(), $"Bridge /tail-state HTTP {(int)res.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            bool isSuccess = !doc.RootElement.TryGetProperty("isSuccess", out var sProp) || sProp.GetBoolean();
            if (!isSuccess)
            {
                string err = doc.RootElement.TryGetProperty("errorDescription", out var eProp) ? eProp.GetString() ?? body : body;
                return new WhatsAppTailStateResult(false, Array.Empty<WhatsAppTailMessage>(), err);
            }

            var list = new List<WhatsAppTailMessage>();
            if (doc.RootElement.TryGetProperty("messages", out var msgsElem) && msgsElem.ValueKind == JsonValueKind.Array)
            {
                int idx = 0;
                foreach (var item in msgsElem.EnumerateArray())
                {
                    int order = item.TryGetProperty("order", out var oProp) && oProp.TryGetInt32(out int oVal) ? oVal : idx;
                    string msgId = item.TryGetProperty("messageId", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty;
                    string? fullId = item.TryGetProperty("fullDataId", out var fProp) ? fProp.GetString() : null;
                    string? marker = item.TryGetProperty("marker", out var mProp) && mProp.ValueKind != JsonValueKind.Null ? mProp.GetString() : null;
                    string? mType = item.TryGetProperty("markerType", out var mtProp) && mtProp.ValueKind != JsonValueKind.Null ? mtProp.GetString() : null;
                    string? mDate = item.TryGetProperty("markerDate", out var mdProp) && mdProp.ValueKind != JsonValueKind.Null ? mdProp.GetString() : null;
                    int? mSeq = item.TryGetProperty("markerSeq", out var msProp) && msProp.ValueKind == JsonValueKind.Number && msProp.TryGetInt32(out int seqVal) ? seqVal : null;
                    string titleLine = item.TryGetProperty("titleLine", out var tlProp) ? tlProp.GetString() ?? string.Empty : string.Empty;
                    string textWithoutMarker = item.TryGetProperty("textWithoutMarker", out var txProp) ? txProp.GetString() ?? string.Empty : string.Empty;
                    bool hasImage = item.TryGetProperty("hasImage", out var imgProp) && imgProp.ValueKind == JsonValueKind.True;

                    list.Add(new WhatsAppTailMessage(
                        order,
                        msgId,
                        fullId,
                        marker,
                        mType,
                        mDate,
                        mSeq,
                        titleLine,
                        textWithoutMarker,
                        hasImage
                    ));
                    idx++;
                }
            }

            return new WhatsAppTailStateResult(true, list);
        }
        catch (Exception ex)
        {
            return new WhatsAppTailStateResult(false, Array.Empty<WhatsAppTailMessage>(), $"Failed to read tail-state from bridge: {ex.Message}");
        }
    }

    public async Task<WhatsAppDispatchResult> SweepObsoleteForecastsAsync(
        string channelOrChatId,
        string todayDate,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channelOrChatId))
            throw new ArgumentException("Channel identifier cannot be null or empty.", nameof(channelOrChatId));

        var url = $"{_bridgeBaseUrl}/sweep-forecasts";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            todayDate = todayDate
        };

        return await PostJsonAsync(url, payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WhatsAppDispatchResult> CheckChannelAccessAsync(
        string channelOrChatId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Health check of the bridge
            var healthUrl = $"{_bridgeBaseUrl}/health";
            using var healthReq = new HttpRequestMessage(HttpMethod.Get, healthUrl);
            using var healthRes = await _httpClient.SendAsync(healthReq, cancellationToken).ConfigureAwait(false);

            if (!healthRes.IsSuccessStatusCode)
            {
                return new WhatsAppDispatchResult(false, null, $"WhatsApp Bridge service is unreachable (HTTP {healthRes.StatusCode}). Make sure tools/whatsapp-bridge/run-bridge.ps1 is running.", false);
            }

            string healthBody = await healthRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using (var doc = JsonDocument.Parse(healthBody))
            {
                bool connected = doc.RootElement.TryGetProperty("connected", out var cProp) && cProp.GetBoolean();
                if (!connected)
                {
                    bool hasQr = doc.RootElement.TryGetProperty("hasQr", out var qProp) && qProp.GetBoolean();
                    string msg = hasQr
                        ? "WhatsApp Bridge is waiting for QR code authentication. Please scan the QR code in the bridge terminal."
                        : "WhatsApp Bridge is currently disconnected from WhatsApp Web.";
                    return new WhatsAppDispatchResult(false, null, msg, false);
                }
            }

            // 2. Channel info check
            var channelUrl = $"{_bridgeBaseUrl}/channel-info?channelId={Uri.EscapeDataString(channelOrChatId)}";
            using var chReq = new HttpRequestMessage(HttpMethod.Get, channelUrl);
            using var chRes = await _httpClient.SendAsync(chReq, cancellationToken).ConfigureAwait(false);

            string chBody = await chRes.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (chRes.IsSuccessStatusCode)
            {
                string channelName = channelOrChatId;
                string jid = channelOrChatId;
                try
                {
                    using var doc = JsonDocument.Parse(chBody);
                    if (doc.RootElement.TryGetProperty("name", out var nProp))
                        channelName = nProp.GetString() ?? channelOrChatId;
                    if (doc.RootElement.TryGetProperty("jid", out var jProp))
                        jid = jProp.GetString() ?? channelOrChatId;
                }
                catch { }

                return new WhatsAppDispatchResult(true, ErrorDescription: $"Connected to WhatsApp Channel '{channelName}' (JID: {jid}) via Local Bridge");
            }

            return new WhatsAppDispatchResult(false, null, $"Channel access check failed: {chBody}", false);
        }
        catch (HttpRequestException ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Could not connect to WhatsApp Bridge at {_bridgeBaseUrl}: {ex.Message}. Make sure the bridge is running.", true);
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"WhatsApp Bridge check error: {ex.Message}", true);
        }
    }

    private async Task<WhatsAppDispatchResult> PostJsonAsync(string url, object payload, CancellationToken cancellationToken)
    {
        try
        {
            string json = JsonSerializer.Serialize(payload);
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            string? messageId = null;
            string? opStatus = null;
            string? errDesc = null;
            bool? bodyIsSuccess = null;

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("messageId", out var idElem) && idElem.ValueKind == JsonValueKind.String)
                {
                    messageId = idElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("status", out var stElem) && stElem.ValueKind == JsonValueKind.String)
                {
                    opStatus = stElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("errorDescription", out var dElem) && dElem.ValueKind == JsonValueKind.String)
                {
                    errDesc = dElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("isSuccess", out var isElem) && (isElem.ValueKind == JsonValueKind.True || isElem.ValueKind == JsonValueKind.False))
                {
                    bodyIsSuccess = isElem.GetBoolean();
                }
            }
            catch { }

            if (response.IsSuccessStatusCode && (bodyIsSuccess ?? true))
            {
                return new WhatsAppDispatchResult(true, MessageId: messageId, ErrorDescription: errDesc, OperationStatus: opStatus);
            }

            string err = errDesc ?? responseBody;
            bool isRetryable = opStatus != "windowExpired";
            return new WhatsAppDispatchResult(false, messageId, $"Bridge error: {err}", isRetryable, OperationStatus: opStatus);
        }
        catch (HttpRequestException ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Bridge HTTP connection failed: {ex.Message}", true);
        }
        catch (Exception ex)
        {
            return new WhatsAppDispatchResult(false, null, $"Unexpected error communicating with bridge: {ex.Message}", true);
        }
    }
}
