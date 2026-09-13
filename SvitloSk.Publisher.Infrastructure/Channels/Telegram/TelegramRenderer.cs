using System;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Implements Telegram rendering and formatting rules according to TELEGRAM_RENDERING_SPECIFICATION.
/// Controls HTML markup tags, escaping, and caption character limits.
/// </summary>
public static class TelegramRenderer
{
    public const int MaxCaptionLength = 1024;
    public const int MaxMessageLength = 4096;

    /// <summary>
    /// Formats caption for Telegram photo messages, enforcing the 1024-character limit.
    /// Truncates at a safe word boundary if necessary.
    /// </summary>
    public static string FormatCaption(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= MaxCaptionLength) return text;

        // Truncate at safe word boundary with ellipsis
        int truncateIndex = text.LastIndexOf(' ', MaxCaptionLength - 3);
        if (truncateIndex <= 0) truncateIndex = MaxCaptionLength - 3;
        return text.Substring(0, truncateIndex) + "...";
    }

    /// <summary>
    /// Generates canonical caption for 12-subqueue graphic outage schedule.
    /// </summary>
    public static string FormatGraphicScheduleCaption(string formattedDate)
    {
        return $"Графік знеструмлень на {formattedDate}\n#графік #старокостянтинів #svitlosk";
    }
}
