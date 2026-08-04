using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SvitloSk.Publisher.Core.Domain;

namespace SvitloSk.Publisher.Channels;

public class TelegramAdapter
{
    private readonly string _botToken;
    private readonly string _channelId;
    private readonly MarkdownV2Renderer _renderer;
    private readonly HttpClient _httpClient;

    public TelegramAdapter(string botToken, string channelId, MarkdownV2Renderer renderer)
    {
        _botToken = botToken;
        _channelId = channelId;
        _renderer = renderer;
        _httpClient = new HttpClient();
    }

    public async Task PublishEditionAsync(Edition edition)
    {
        var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
        
        int i = 1;
        foreach (var pub in edition.Publications)
        {
            Console.WriteLine($"         Publishing publication {i} of {edition.Publications.Count} ({pub.Locality})...");
            
            var escapedText = _renderer.Escape(pub.Content);
            var finalMessage = $"⚡️ *{_renderer.Escape(pub.Locality)}*\n\n{escapedText}";
            
            Console.WriteLine($"           [Adapter] ChannelId: {_channelId}");
            Console.WriteLine($"           [Adapter] Message Length: {finalMessage.Length} chars");
            Console.WriteLine($"           [Adapter] ParseMode: MarkdownV2");

            var payload = new
            {
                chat_id = _channelId,
                text = finalMessage,
                parse_mode = "MarkdownV2"
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"           [Error] Telegram API rejected {pub.Locality}: {response.StatusCode} - {responseString}");
            }
            else
            {
                Console.WriteLine($"           [Success] Published {pub.Locality} to Telegram.");
            }

            i++;
            await Task.Delay(1500);
        }
    }
}
