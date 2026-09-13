using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Manages Telegram Discussion Group operations per TELEGRAM_DISCUSSION_SPECIFICATION.
/// Deletes auto-forwarded channel messages in the discussion group to close comment threads.
/// </summary>
public class TelegramDiscussionManager
{
    private readonly HttpClient _httpClient;
    private readonly string _botToken;
    private readonly string _baseUrl;

    public TelegramDiscussionManager(HttpClient httpClient, string botToken, string baseUrl = "https://api.telegram.org")
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _botToken = botToken ?? throw new ArgumentNullException(nameof(botToken));
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public async Task<TelegramDispatchResult> CloseCommentsAsync(
        string discussionGroupId,
        int channelMessageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(discussionGroupId))
            throw new ArgumentException("Discussion group identifier cannot be null or empty.", nameof(discussionGroupId));

        for (int attempt = 1; attempt <= 4; attempt++)
        {
            await Task.Delay(1200, cancellationToken).ConfigureAwait(false);

            int? discussionMsgId = await FindDiscussionMessageIdAsync(discussionGroupId, channelMessageId, cancellationToken).ConfigureAwait(false);
            if (discussionMsgId.HasValue)
            {
                bool deleted = await DeleteDiscussionMessageAsync(discussionGroupId, discussionMsgId.Value, cancellationToken).ConfigureAwait(false);
                if (deleted)
                {
                    Console.WriteLine($"[INFO] Comments successfully closed in discussion group (deleted discussion msg {discussionMsgId.Value} for channel msg {channelMessageId}).");
                    return new TelegramDispatchResult(true, null, null, false);
                }
            }
            else
            {
                Console.WriteLine($"[DEBUG] CloseComments attempt {attempt} for msg {channelMessageId}: forward not found in updates yet.");
            }
        }

        // Fallback attempt with channelMessageId directly
        bool fallbackDeleted = await DeleteDiscussionMessageAsync(discussionGroupId, channelMessageId, cancellationToken).ConfigureAwait(false);
        if (fallbackDeleted)
        {
            Console.WriteLine($"[INFO] Comments successfully closed via direct ID fallback for channel msg {channelMessageId}.");
            return new TelegramDispatchResult(true, null, null, false);
        }

        Console.WriteLine($"[WARN] CloseComments could not find or delete discussion message for channel msg {channelMessageId}.");
        return new TelegramDispatchResult(false, null, "Could not locate or delete discussion message", false);
    }

    private async Task<int?> FindDiscussionMessageIdAsync(string discussionGroupId, int channelMessageId, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{_baseUrl}/bot{_botToken}/getUpdates?allowed_updates=[\"message\"]&limit=100";
            using var resp = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("result", out var resultArr) || resultArr.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            int? highestUpdateId = null;
            int? matchedDiscussionMsgId = null;

            foreach (var update in resultArr.EnumerateArray())
            {
                if (update.TryGetProperty("update_id", out var updateIdElem))
                {
                    int uId = updateIdElem.GetInt32();
                    if (!highestUpdateId.HasValue || uId > highestUpdateId.Value)
                    {
                        highestUpdateId = uId;
                    }
                }

                if (update.TryGetProperty("message", out var msgElem))
                {
                    if (msgElem.TryGetProperty("chat", out var chatElem) && chatElem.TryGetProperty("id", out var chatIdElem))
                    {
                        string chatIdStr = chatIdElem.GetRawText();
                        if (chatIdStr.Equals(discussionGroupId, StringComparison.OrdinalIgnoreCase) || 
                            discussionGroupId.EndsWith(chatIdStr.TrimStart('-'), StringComparison.OrdinalIgnoreCase) ||
                            chatIdStr.EndsWith(discussionGroupId.TrimStart('-'), StringComparison.OrdinalIgnoreCase))
                        {
                            bool isMatch = false;

                            if (msgElem.TryGetProperty("forward_from_message_id", out var fwdId) && fwdId.GetInt32() == channelMessageId)
                            {
                                isMatch = true;
                            }
                            else if (msgElem.TryGetProperty("forward_origin", out var origin) && 
                                     origin.TryGetProperty("message_id", out var origMsgId) && 
                                     origMsgId.GetInt32() == channelMessageId)
                            {
                                isMatch = true;
                            }

                            if (isMatch && msgElem.TryGetProperty("message_id", out var discMsgIdElem))
                            {
                                matchedDiscussionMsgId = discMsgIdElem.GetInt32();
                            }
                        }
                    }
                }
            }

            if (highestUpdateId.HasValue)
            {
                try
                {
                    _ = await _httpClient.GetAsync($"{_baseUrl}/bot{_botToken}/getUpdates?offset={highestUpdateId.Value + 1}&limit=1", cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }

            return matchedDiscussionMsgId;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> DeleteDiscussionMessageAsync(string discussionGroupId, int discussionMessageId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_baseUrl}/bot{_botToken}/deleteMessage";
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(discussionGroupId), "chat_id");
            content.Add(new StringContent(discussionMessageId.ToString()), "message_id");

            using var resp = await _httpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode)
            {
                return true;
            }
            string body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"[DEBUG] deleteMessage in discussion group returned ({resp.StatusCode}): {body}");
            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] deleteMessage exception in discussion group: {ex.Message}");
            return false;
        }
    }
}
