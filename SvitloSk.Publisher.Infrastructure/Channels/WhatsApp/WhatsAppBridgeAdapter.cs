using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// HTTP adapter for local WhatsApp Bridge microservice (Baileys / Node.js).
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
        // WhatsApp Channel protocol does not reliably support inline message mutation over web-sessions.
        // Returning failure triggers the Pipeline's built-in 30-min Delete+Create rollover fallback.
        await Task.CompletedTask;
        return new WhatsAppDispatchResult(false, null, "In-place edit not supported over WhatsApp Web session; falling back to Delete+Create rollover.", false);
    }

    public async Task<WhatsAppDispatchResult> DeleteMessageAsync(
        string channelOrChatId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(messageId))
            return new WhatsAppDispatchResult(true, null, "No message ID specified for deletion", false);

        var url = $"{_bridgeBaseUrl}/delete";
        var payload = new
        {
            channelOrChatId = channelOrChatId,
            messageId = messageId
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

            if (response.IsSuccessStatusCode)
            {
                string? messageId = null;
                try
                {
                    using var doc = JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("messageId", out var idElem))
                    {
                        messageId = idElem.GetString();
                    }
                }
                catch { }

                return new WhatsAppDispatchResult(true, MessageId: messageId);
            }

            string err = responseBody;
            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("errorDescription", out var dElem))
                {
                    err = dElem.GetString() ?? responseBody;
                }
            }
            catch { }

            return new WhatsAppDispatchResult(false, null, $"Bridge error: {err}", false);
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
