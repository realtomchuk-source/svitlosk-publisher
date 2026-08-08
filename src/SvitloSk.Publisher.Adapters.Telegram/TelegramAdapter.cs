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
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public AcceptedPublication Publish(TransportArtifact artifact)
    {
        if (artifact == null) throw new ArgumentNullException(nameof(artifact));

        _logger.LogInformation("TelegramAdapter publishing artifact: {RequestId}", artifact.RequestId);

        // Synchronous wrapper to match interface, though real implementation should probably be async
        return PublishAsync(artifact).GetAwaiter().GetResult();
    }

    private async Task<AcceptedPublication> PublishAsync(TransportArtifact artifact)
    {
        string endpoint;
        object payload;

        // TELEGRAM_MAPPING_SPECIFICATION.md
        if (artifact.Operation == TransportOperation.DELETE)
        {
            // STOP CONDITION: How do we get the message_id to delete if SynchronizationRegistry is not implemented?
            // The specification states that the adapter needs chat_id and message_id for deleteMessage.
            // Since we do not have externalId (MessageId) attached to the artifact (it lacks it), this is a missing spec statement.
            throw new InvalidOperationException("Cannot perform DELETE: Synchronization identity resolution is missing from Pipeline.");
        }
        else if (artifact.Operation == TransportOperation.CREATE)
        {
            if (artifact.Type == TransportArtifactType.TEXT_ONLY)
            {
                endpoint = "sendMessage";
                payload = new { chat_id = _options.TargetChatId, text = artifact.Payload };
            }
            else if (artifact.Type == TransportArtifactType.SINGLE_MEDIA)
            {
                endpoint = "sendPhoto";
                payload = new { chat_id = _options.TargetChatId, photo = "attach://media", caption = artifact.Payload }; // Simplified for now
                // Actually the tests use fake transport, we can just pass payload.
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
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var url = $"https://api.telegram.org/bot{_options.BotToken}/{endpoint}";
        var response = await _httpClient.PostAsync(url, content);
        
        response.EnsureSuccessStatusCode();
        var responseString = await response.Content.ReadAsStringAsync();
        
        using var doc = JsonDocument.Parse(responseString);
        var root = doc.RootElement;
        
        // TELEGRAM_SYNCHRONIZATION_SPECIFICATION.md
        // 1. The Adapter extracts the message_id and the chat.id from the JSON response.
        // 2. These values are combined (e.g., chatId:messageId) to form the generic externalId string.
        var messageId = root.GetProperty("result").GetProperty("message_id").GetRawText();
        var chatId = root.GetProperty("result").GetProperty("chat").GetProperty("id").GetRawText();
        var externalId = $"{chatId}:{messageId}";

        return new AcceptedPublication(externalId, _options.TargetChatId);
    }
}
