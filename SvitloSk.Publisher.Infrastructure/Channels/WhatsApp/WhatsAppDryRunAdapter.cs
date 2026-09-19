using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// Safe dry-run adapter for WhatsApp Messenger.
/// Simulates successful dispatch operations, generates mock message identifiers,
/// and prevents actual network mutations during testing.
/// </summary>
public class WhatsAppDryRunAdapter : IWhatsAppAdapter
{
    private readonly ConcurrentBag<string> _dispatchedMessages = new();

    public IReadOnlyCollection<string> DispatchedMessages => _dispatchedMessages;

    public Task<WhatsAppDispatchResult> SendTextMessageAsync(
        string channelOrChatId,
        string text,
        CancellationToken cancellationToken = default)
    {
        string id = $"wa_mock_{Guid.NewGuid():N}";
        _dispatchedMessages.Add($"[TEXT] To: {channelOrChatId} | Id: {id} | Length: {text?.Length ?? 0}");
        Console.WriteLine($"[DryRun][WhatsApp] SendTextMessageAsync to '{channelOrChatId}' (MockId: {id})");
        return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: id));
    }

    public Task<WhatsAppDispatchResult> SendMediaMessageAsync(
        string channelOrChatId,
        string caption,
        byte[] mediaBytes,
        string mimeType = "image/png",
        CancellationToken cancellationToken = default)
    {
        string id = $"wa_mock_media_{Guid.NewGuid():N}";
        _dispatchedMessages.Add($"[MEDIA] To: {channelOrChatId} | Id: {id} | Bytes: {mediaBytes?.Length ?? 0}");
        Console.WriteLine($"[DryRun][WhatsApp] SendMediaMessageAsync to '{channelOrChatId}' (MockId: {id}, Bytes: {mediaBytes?.Length ?? 0})");
        return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: id));
    }

    public Task<WhatsAppDispatchResult> UpdateTextMessageAsync(
        string channelOrChatId,
        string messageId,
        string text,
        CancellationToken cancellationToken = default)
    {
        _dispatchedMessages.Add($"[UPDATE] To: {channelOrChatId} | Id: {messageId} | Length: {text?.Length ?? 0}");
        Console.WriteLine($"[DryRun][WhatsApp] UpdateTextMessageAsync in '{channelOrChatId}' for MsgId '{messageId}'");
        return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: messageId));
    }

    public Task<WhatsAppDispatchResult> DeleteMessageAsync(
        string channelOrChatId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        _dispatchedMessages.Add($"[DELETE] To: {channelOrChatId} | Id: {messageId}");
        Console.WriteLine($"[DryRun][WhatsApp] DeleteMessageAsync in '{channelOrChatId}' for MsgId '{messageId}'");
        return Task.FromResult(new WhatsAppDispatchResult(true, MessageId: messageId));
    }

    public Task<WhatsAppDispatchResult> CheckChannelAccessAsync(
        string channelOrChatId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[DryRun][WhatsApp] CheckChannelAccessAsync for '{channelOrChatId}' -> OK");
        return Task.FromResult(new WhatsAppDispatchResult(true, ErrorDescription: $"Connected to WhatsApp Channel '{channelOrChatId}' (DryRun)"));
    }
}
