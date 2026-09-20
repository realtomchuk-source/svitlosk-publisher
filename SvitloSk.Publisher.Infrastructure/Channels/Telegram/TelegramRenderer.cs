using System;
using System.Text;
using SvitloSk.Publisher.Core.Domain;

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

    /// <summary>
    /// Renders technical system status message for Telegram.
    /// </summary>
    public static string RenderSystemStatus(SystemStatusModel model)
    {
        var localTime = model.LastUpdatedUtc.AddHours(3);
        return $"<b>Останнє оновлення журналу:</b> {localTime:HH:mm}\n<b>Стан моніторингу:</b> {model.MonitoringState}";
    }

    /// <summary>
    /// Renders clean domain JournalHeaderModel into valid Telegram HTML format.
    /// </summary>
    public static string RenderJournalHeader(JournalHeaderModel model)
    {
        var sb = new StringBuilder();

        if (model.PlannedSettlements.Count > 0)
        {
            string pList = string.Join(", ", model.PlannedSettlements);
            sb.AppendLine($"<b>Планові знеструмлення:</b> {pList}");
        }
        else
        {
            sb.AppendLine("<b>Планові знеструмлення:</b> відсутні");
        }

        sb.AppendLine();

        if (model.EmergencySettlements.Count > 0)
        {
            string eList = string.Join(", ", model.EmergencySettlements);
            sb.AppendLine($"<b>Аварійні знеструмлення:</b> {eList}");
        }
        else
        {
            sb.AppendLine("<b>Аварійні знеструмлення:</b> відсутні");
        }

        return sb.ToString().TrimEnd();
    }
}
