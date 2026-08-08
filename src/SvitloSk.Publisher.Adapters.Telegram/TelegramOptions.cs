namespace SvitloSk.Publisher.Adapters.Telegram;

public class TelegramOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string TargetChatId { get; set; } = string.Empty;
}
