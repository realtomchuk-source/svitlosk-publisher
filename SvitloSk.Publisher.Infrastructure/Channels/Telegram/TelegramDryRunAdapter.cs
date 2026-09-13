using System;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Dry-run emulator for TelegramAdapter. Simulates message sending without actual network calls.
/// </summary>
public class FakeTelegramDryRunAdapter : ITelegramAdapter
{
    private static readonly Random _random = new Random();

    public Task<TelegramDispatchResult> SendAsync(string chatNameOrId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
    {
        int msgId = _random.Next(1000, 9999);
        Console.WriteLine($"[DryRun-Telegram] Send to {chatNameOrId} -> Assigned MessageId: {msgId}");
        return Task.FromResult(new TelegramDispatchResult(true, msgId, null, false));
    }

    public Task<TelegramDispatchResult> UpdateAsync(string chatNameOrId, int messageId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DryRun-Telegram] Update message {messageId} in {chatNameOrId}");
        return Task.FromResult(new TelegramDispatchResult(true, messageId, null, false));
    }

    public Task<TelegramDispatchResult> DeleteAsync(string chatNameOrId, int messageId, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DryRun-Telegram] Delete message {messageId} in {chatNameOrId}");
        return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
    }

    public Task<TelegramDispatchResult> CloseCommentsAsync(string discussionGroupId, int channelMessageId, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DryRun-Telegram] Close comments in discussion group {discussionGroupId} for message {channelMessageId}");
        return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
    }
}

/// <summary>
/// Dry-run emulator for IGraphicPublisherDispatcher. Simulates 12-subqueue graphic operations without network calls.
/// </summary>
public class FakeGraphicDryRunDispatcher : IGraphicPublisherDispatcher
{
    private static readonly Random _random = new Random();

    public Task<TelegramDispatchResult> DispatchGraphicAsync(GraphicOperationPayload payload, CancellationToken cancellationToken = default)
    {
        int msgId = payload.TelegramMessageId ?? _random.Next(1000, 9999);
        Console.WriteLine($"[DryRun-Graphic] {payload.OperationType} for scope '{payload.TerritoryId}' in {payload.ChatNameOrId} -> MessageId: {msgId}");
        return Task.FromResult(new TelegramDispatchResult(true, msgId, null, false));
    }
}
