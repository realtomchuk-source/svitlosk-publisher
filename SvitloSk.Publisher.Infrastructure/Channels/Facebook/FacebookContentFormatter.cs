using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.Facebook;

/// <summary>
/// Formats publication content specifically for the Facebook News Feed:
/// - Strips HTML markup tags and Telegram-specific escapes.
/// - Uses standard UTF-8 emojis, clean bullet points, and high readability.
/// - Produces consolidated day digest and dedicated 12-subqueue graphic post captions.
/// </summary>
public static class FacebookContentFormatter
{
    public static string StripHtml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        string noTags = Regex.Replace(input, "<.*?>", string.Empty);
        return HttpUtility.HtmlDecode(noTags).Trim();
    }

    public static string FormatGraphicCaption(string formattedDate)
    {
        return $"⚡ ГРАФІК ЗНЕСТРУМЛЕНЬ — {formattedDate}\n\n" +
               "Опубліковано детальний 12-підчерговий графік погодинних відключень електроенергії у Старокостянтинівській міській територіальній громаді.\n\n" +
               "#графік #старокостянтинів #підчерги #svitlosk";
    }

    public static string FormatConsolidatedTodayPost(
        string editionDate,
        IReadOnlyList<TransformedPackage> packages,
        DateTime? lastUpdatedUtc = null)
    {
        var sb = new StringBuilder();
        string formattedDate = EditorialContentTransformer.FormatDate(editionDate);

        sb.AppendLine($"⚡ ЖУРНАЛ ЗНЕСТРУМЛЕНЬ — {formattedDate}");
        sb.AppendLine("📍 Старокостянтинівська міська територіальна громада");
        sb.AppendLine();

        // Territory packages (excluding header or tomorrow posts)
        var territoryPackages = packages
            .Where(p => !p.TerritoryId.Equals("journal_header", StringComparison.OrdinalIgnoreCase) &&
                        !p.TerritoryId.StartsWith("tomorrow_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (territoryPackages.Count == 0)
        {
            sb.AppendLine("✅ Планових та аварійних знеструмлень у громаді наразі не зафіксовано. Електропостачання стабільне.");
        }
        else
        {
            foreach (var pkg in territoryPackages)
            {
                string cleanContent = StripHtml(pkg.Content);
                if (!string.IsNullOrWhiteSpace(cleanContent))
                {
                    sb.AppendLine(cleanContent);
                    sb.AppendLine();
                }
            }
        }

        var localTime = (lastUpdatedUtc ?? DateTime.UtcNow).AddHours(3);
        sb.AppendLine($"🕒 Останнє оновлення: {localTime:HH:mm}");
        sb.AppendLine("#svitlosk #старокостянтинів #відключення #графік");

        return sb.ToString().TrimEnd();
    }

    public static string FormatConsolidatedTomorrowPost(
        string tomorrowDate,
        IReadOnlyList<TransformedPackage> packages)
    {
        var sb = new StringBuilder();
        string formattedDate = EditorialContentTransformer.FormatDate(tomorrowDate);

        sb.AppendLine($"⚡ ПРОГНОЗ ВІДКЛЮЧЕНЬ НА ЗАВТРА — {formattedDate}");
        sb.AppendLine("📍 Старокостянтинівська міська територіальна громада");
        sb.AppendLine();

        var tomorrowPackages = packages
            .Where(p => p.TerritoryId.StartsWith("tomorrow_", StringComparison.OrdinalIgnoreCase) &&
                        !p.TerritoryId.Equals("tomorrow_separator", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (tomorrowPackages.Count == 0)
        {
            sb.AppendLine("✅ На завтра планових знеструмлень у громаді не передбачається.");
        }
        else
        {
            foreach (var pkg in tomorrowPackages)
            {
                string cleanContent = StripHtml(pkg.Content);
                if (!string.IsNullOrWhiteSpace(cleanContent))
                {
                    sb.AppendLine(cleanContent);
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("⚠️ Зверніть увагу: графік може бути скориговано відповідно до розпоряджень НЕК «Укренерго».");
        sb.AppendLine("#прогноз #завтра #старокостянтинів #svitlosk");

        return sb.ToString().TrimEnd();
    }

    public static string FormatTerritoryPost(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        string clean = StripHtml(input);

        // Enhance headers with distinct visual indicators for Facebook feed
        clean = Regex.Replace(clean, @"^(Місто Старокостянтинів)", "🏙️ $1", RegexOptions.Multiline);
        clean = Regex.Replace(clean, @"^([А-Яа-яІіЇїЄєҐґ'\-]+ старостинський округ)", "📍 $1", RegexOptions.Multiline);
        clean = Regex.Replace(clean, @"\b(АВАРІЙНІ ЗНЕСТРУМЛЕННЯ)\b", "🚨 $1");
        clean = Regex.Replace(clean, @"\b(ПЛАНОВІ ЗНЕСТРУМЛЕННЯ)\b", "📋 $1");

        var sb = new StringBuilder(clean);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("⚡ SvitloSk Journal | Старокостянтинівська міська територіальна громада");
        sb.AppendLine("#відключення #старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd();
    }
}
