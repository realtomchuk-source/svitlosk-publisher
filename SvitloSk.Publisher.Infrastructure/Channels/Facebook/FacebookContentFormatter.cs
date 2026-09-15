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

    public static string? FormatFacebookEmergencyPost(
        string editionDate,
        IReadOnlyList<AggregatedTerritoryData> territories,
        EditorialContentTransformer? transformer = null,
        DateTime? lastUpdatedUtc = null)
    {
        var emergencyTerritories = territories
            .Where(t => t.EmergencyRecords != null && t.EmergencyRecords.Count > 0)
            .ToList();

        if (emergencyTerritories.Count == 0)
        {
            return null;
        }

        transformer ??= new EditorialContentTransformer();
        string formattedDate = EditorialContentTransformer.FormatDate(editionDate);

        var sb = new StringBuilder();
        sb.AppendLine($"🚨 АВАРІЙНІ ЗНЕСТРУМЛЕННЯ — {formattedDate}");
        sb.AppendLine("📍 Старокостянтинівська міська територіальна громада");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        // 1. City of Starokostiantyniv first (top priority)
        var city = emergencyTerritories.FirstOrDefault(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase));
        if (city != null)
        {
            sb.AppendLine("🏙️ МІСТО СТАРОКОСТЯНТИНІВ");
            var cityOnly = city with { PlannedRecords = Array.Empty<OutageRecord>() };
            string cityHtml = transformer.RenderAggregatedTerritoryPost(cityOnly);
            string cityClean = CleanBodyWithoutTitle(cityHtml, city.CanonicalName);
            if (!string.IsNullOrWhiteSpace(cityClean))
            {
                sb.AppendLine(cityClean);
                sb.AppendLine();
            }
        }

        // 2. Rural Starosta districts
        var ruralDistricts = emergencyTerritories
            .Where(t => !t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (ruralDistricts.Count > 0)
        {
            sb.AppendLine("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ");
            sb.AppendLine();

            foreach (var district in ruralDistricts)
            {
                sb.AppendLine($"📍 {district.CanonicalName.ToUpperInvariant()}");
                var distOnly = district with { PlannedRecords = Array.Empty<OutageRecord>() };
                string distHtml = transformer.RenderAggregatedTerritoryPost(distOnly);
                string distClean = CleanBodyWithoutTitle(distHtml, district.CanonicalName);
                if (!string.IsNullOrWhiteSpace(distClean))
                {
                    sb.AppendLine(distClean);
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("ℹ️ ТЕХНІЧНА ІНФОРМАЦІЯ:");
        sb.AppendLine($"• Джерело даних: АТ «Хмельницькобленерго»");
        if (lastUpdatedUtc.HasValue)
        {
            var localTime = lastUpdatedUtc.Value.AddHours(3);
            sb.AppendLine($"• Час оновлення: {localTime:HH:mm} (Київ)");
        }
        sb.AppendLine($"• Моніторинг: SvitloSk Автоматичний диспетчер");
        sb.AppendLine($"• Оперативні сповіщення у Telegram: https://t.me/svitlosk");
        sb.AppendLine();
        sb.AppendLine("#аварійнівідключення #старокостянтинів #громада #хмельницькобленерго #svitlosk");

        return sb.ToString().TrimEnd();
    }

    public static string FormatFacebookPlannedPost(
        string editionDate,
        IReadOnlyList<AggregatedTerritoryData> territories,
        EditorialContentTransformer? transformer = null,
        DateTime? lastUpdatedUtc = null)
    {
        transformer ??= new EditorialContentTransformer();
        string formattedDate = EditorialContentTransformer.FormatDate(editionDate);

        var sb = new StringBuilder();
        sb.AppendLine($"⚡ ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — {formattedDate}");
        sb.AppendLine("📍 Старокостянтинівська міська територіальна громада");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        var plannedTerritories = territories
            .Where(t => t.PlannedRecords != null && t.PlannedRecords.Count > 0)
            .ToList();

        if (plannedTerritories.Count == 0)
        {
            sb.AppendLine("✅ Станом на сьогодні планових знеструмлень у громаді не заплановано.");
            sb.AppendLine("Електропостачання споживачів здійснюється у штатному режимі.");
            sb.AppendLine();
        }
        else
        {
            // 1. City of Starokostiantyniv first (top priority)
            var city = plannedTerritories.FirstOrDefault(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase));
            if (city != null)
            {
                sb.AppendLine("🏙️ МІСТО СТАРОКОСТЯНТИНІВ");
                var cityOnly = city with { EmergencyRecords = Array.Empty<OutageRecord>() };
                string cityHtml = transformer.RenderAggregatedTerritoryPost(cityOnly);
                string cityClean = CleanBodyWithoutTitle(cityHtml, city.CanonicalName);
                if (!string.IsNullOrWhiteSpace(cityClean))
                {
                    sb.AppendLine(cityClean);
                    sb.AppendLine();
                }
            }

            // 2. Rural Starosta districts
            var ruralDistricts = plannedTerritories
                .Where(t => !t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (ruralDistricts.Count > 0)
            {
                sb.AppendLine("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ");
                sb.AppendLine();

                foreach (var district in ruralDistricts)
                {
                    sb.AppendLine($"📍 {district.CanonicalName.ToUpperInvariant()}");
                    var distOnly = district with { EmergencyRecords = Array.Empty<OutageRecord>() };
                    string distHtml = transformer.RenderAggregatedTerritoryPost(distOnly);
                    string distClean = CleanBodyWithoutTitle(distHtml, district.CanonicalName);
                    if (!string.IsNullOrWhiteSpace(distClean))
                    {
                        sb.AppendLine(distClean);
                        sb.AppendLine();
                    }
                }
            }
        }

        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("ℹ️ ТЕХНІЧНА ІНФОРМАЦІЯ:");
        sb.AppendLine($"• Джерело даних: АТ «Хмельницькобленерго»");
        if (lastUpdatedUtc.HasValue)
        {
            var localTime = lastUpdatedUtc.Value.AddHours(3);
            sb.AppendLine($"• Час оновлення: {localTime:HH:mm} (Київ)");
        }
        sb.AppendLine($"• Оперативні сповіщення у Telegram: https://t.me/svitlosk");
        sb.AppendLine();
        sb.AppendLine("#відключення #плановівідключення #старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd();
    }

    public static string? FormatFacebookTomorrowPost(
        string tomorrowDate,
        IReadOnlyList<AggregatedTerritoryData> territories,
        EditorialContentTransformer? transformer = null,
        DateTime? lastUpdatedUtc = null)
    {
        if (territories == null || territories.Count == 0)
        {
            return null;
        }

        bool anyRecords = territories.Any(t => (t.PlannedRecords != null && t.PlannedRecords.Count > 0) || (t.EmergencyRecords != null && t.EmergencyRecords.Count > 0));
        if (!anyRecords)
        {
            return null;
        }

        transformer ??= new EditorialContentTransformer();
        string formattedDate = EditorialContentTransformer.FormatDate(tomorrowDate);

        var sb = new StringBuilder();
        sb.AppendLine($"🔮 ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА — {formattedDate}");
        sb.AppendLine("📍 Старокостянтинівська міська територіальна громада");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine();

        // 1. City first
        var city = territories.FirstOrDefault(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) &&
            ((t.PlannedRecords != null && t.PlannedRecords.Count > 0) || (t.EmergencyRecords != null && t.EmergencyRecords.Count > 0)));
        if (city != null)
        {
            sb.AppendLine("🏙️ МІСТО СТАРОКОСТЯНТИНІВ");
            string cityHtml = transformer.RenderAggregatedTerritoryPost(city, isTomorrow: true, tomorrowDate: tomorrowDate);
            string cityClean = CleanBodyWithoutTitle(cityHtml, city.CanonicalName);
            if (!string.IsNullOrWhiteSpace(cityClean))
            {
                sb.AppendLine(cityClean);
                sb.AppendLine();
            }
        }

        // 2. Rural Starosta districts
        var ruralDistricts = territories
            .Where(t => !t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) &&
                ((t.PlannedRecords != null && t.PlannedRecords.Count > 0) || (t.EmergencyRecords != null && t.EmergencyRecords.Count > 0)))
            .OrderBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (ruralDistricts.Count > 0)
        {
            sb.AppendLine("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ");
            sb.AppendLine();

            foreach (var district in ruralDistricts)
            {
                sb.AppendLine($"📍 {district.CanonicalName.ToUpperInvariant()}");
                string distHtml = transformer.RenderAggregatedTerritoryPost(district, isTomorrow: true, tomorrowDate: tomorrowDate);
                string distClean = CleanBodyWithoutTitle(distHtml, district.CanonicalName);
                if (!string.IsNullOrWhiteSpace(distClean))
                {
                    sb.AppendLine(distClean);
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("⚠️ Зверніть увагу: графік та обсяги відключень можуть бути скориговані відповідно до поточних розпоряджень НЕК «Укренерго».");
        sb.AppendLine();
        sb.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        sb.AppendLine("ℹ️ ТЕХНІЧНА ІНФОРМАЦІЯ:");
        sb.AppendLine($"• Джерело даних: АТ «Хмельницькобленерго» / НЕК «Укренерго»");
        if (lastUpdatedUtc.HasValue)
        {
            var localTime = lastUpdatedUtc.Value.AddHours(3);
            sb.AppendLine($"• Час оновлення: {localTime:HH:mm} (Київ)");
        }
        sb.AppendLine($"• Оперативні сповіщення у Telegram: https://t.me/svitlosk");
        sb.AppendLine();
        sb.AppendLine("#прогноз #відключення #старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd();
    }

    private static string CleanBodyWithoutTitle(string html, string canonicalName)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        string clean = StripHtml(html);

        var lines = clean.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        var resultLines = new List<string>();
        bool skippedTitle = false;

        foreach (var rawLine in lines)
        {
            string trimmed = rawLine.Trim();
            if (!skippedTitle && (trimmed.Equals(canonicalName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                  trimmed.Equals($"Місто {canonicalName}".Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                skippedTitle = true;
                continue;
            }

            if (trimmed.StartsWith("АВАРІЙНІ ЗНЕСТРУМЛЕННЯ", StringComparison.OrdinalIgnoreCase))
            {
                resultLines.Add($"🚨 {trimmed}");
            }
            else if (trimmed.StartsWith("ПЛАНОВІ ЗНЕСТРУМЛЕННЯ", StringComparison.OrdinalIgnoreCase))
            {
                resultLines.Add($"📋 {trimmed}");
            }
            else
            {
                resultLines.Add(rawLine);
            }
        }

        return string.Join("\n", resultLines).Trim();
    }
}
