using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Channels;

namespace SvitloSk.Publisher.Adapters.Telegram;

public class TelegramAdapter : IPublicationPort
{
    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramAdapter> _logger;

    public TelegramAdapter(HttpClient httpClient, IOptions<TelegramOptions> options, ILogger<TelegramAdapter> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_options.BotToken))
            throw new InvalidOperationException("Telegram BotToken is missing from configuration.");
        if (string.IsNullOrWhiteSpace(_options.TargetChatId))
            throw new InvalidOperationException("Telegram TargetChatId is missing from configuration.");
    }

    public Task<AcceptedPublication> PublishAsync(TransportArtifact artifact, CancellationToken cancellationToken = default)
    {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));

        _logger.LogInformation("TelegramAdapter publishing artifact: {RequestId}", artifact.RequestId);

        return CorePublishAsync(artifact, cancellationToken);
    }

    private string EscapeMarkdownV2(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        var specialChars = new[] { '_', '*', '[', ']', '(', ')', '~', '\\', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!' };
        var sb = new StringBuilder(text.Length * 2);
        
        foreach (var c in text)
        {
            if (Array.IndexOf(specialChars, c) >= 0)
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        
        return sb.ToString();
    }

    private async Task<AcceptedPublication> CorePublishAsync(TransportArtifact artifact, CancellationToken cancellationToken)
    {
        string endpoint;
        object payload;

        // TELEGRAM_MAPPING_SPECIFICATION.md
        if (artifact.Operation == TransportOperation.DELETE)
        {
            if (string.IsNullOrWhiteSpace(artifact.ExternalIdentity)) 
            {
                 throw new InvalidOperationException("Cannot perform DELETE: missing external identity.");
            }
            
            var parts = artifact.ExternalIdentity.Split(':');
            string chatId = _options.TargetChatId;
            string messageId = artifact.ExternalIdentity;
            
            if (parts.Length == 2)
            {
                 chatId = parts[0];
                 messageId = parts[1];
            }

            endpoint = "deleteMessage";
            payload = new { chat_id = chatId, message_id = int.Parse(messageId) };
        }
        else if (artifact.Operation == TransportOperation.CREATE)
        {
            if (string.IsNullOrWhiteSpace(artifact.Payload))
            {
                throw new InvalidOperationException("Cannot publish empty message.");
            }

            var escapedPayload = EscapeMarkdownV2(artifact.Payload);

            if (artifact.Type == TransportArtifactType.TEXT_ONLY)
            {
                endpoint = "sendMessage";
                payload = new { chat_id = _options.TargetChatId, text = escapedPayload, parse_mode = "MarkdownV2" };
            }
            else
            {
                throw new NotSupportedException($"Artifact type {artifact.Type} is not supported.");
            }
        }
        else if (artifact.Operation == TransportOperation.UPDATE)
        {
            throw new InvalidOperationException("Cannot perform UPDATE: Synchronization identity resolution is missing from Pipeline.");
        }
        else
        {
            throw new NotSupportedException($"Operation {artifact.Operation} is not supported.");
        }

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"https://api.telegram.org/bot{_options.BotToken}/{endpoint}";
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Telegram API HTTP Error: {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Telegram API returned {(int)response.StatusCode}: {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken);
        
        try
        {
            using var doc = JsonDocument.Parse(responseString);
            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            {
                var description = root.TryGetProperty("description", out var descProp) ? descProp.GetString() : "Unknown";
                _logger.LogError("Telegram API Logic Error: ok=false, description: {Description}", description);
                throw new InvalidOperationException($"Telegram API returned ok=false: {description}");
            }
            
            if (!root.TryGetProperty("result", out var resultObj))
            {
                _logger.LogError("Telegram API response missing 'result' object.");
                throw new InvalidOperationException("Telegram API response missing 'result' object.");
            }

            if (artifact.Operation == TransportOperation.DELETE)
            {
                if (resultObj.ValueKind == JsonValueKind.True || resultObj.ValueKind == JsonValueKind.False)
                {
                    _logger.LogInformation("Successfully processed DELETE artifact {RequestId} with result {Result}", artifact.RequestId, resultObj.GetBoolean());
                    return new AcceptedPublication(artifact.ExternalIdentity ?? string.Empty, _options.TargetChatId);
                }
            }

            if (!resultObj.TryGetProperty("message_id", out var msgIdProp))
            {
                _logger.LogError("Telegram API response missing 'message_id' property.");
                throw new InvalidOperationException("Telegram API response missing 'message_id' property.");
            }

            if (!resultObj.TryGetProperty("chat", out var chatObj) || !chatObj.TryGetProperty("id", out var chatIdProp))
            {
                _logger.LogError("Telegram API response missing 'chat.id' property.");
                throw new InvalidOperationException("Telegram API response missing 'chat.id' property.");
            }

            var messageId = msgIdProp.GetRawText();
            var chatId = chatIdProp.GetRawText();
            var externalId = $"{chatId}:{messageId}";

            _logger.LogInformation("Successfully published artifact {RequestId} with identity {ExternalId}", artifact.RequestId, externalId);
            return new AcceptedPublication(externalId, _options.TargetChatId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Malformed JSON from Telegram API.");
            throw new InvalidOperationException("Malformed JSON from Telegram API.", ex);
        }
    }
}
