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

    public static string FormatOutageTimeRange(string timePart)
    {
        if (string.IsNullOrWhiteSpace(timePart)) return string.Empty;

        // Matches "з 09:00 до 20:00", "09:00 - 20:00", "14:39 – 17:39"
        var match = Regex.Match(timePart, @"(?:з\s*)?(\d{1,2}:\d{2})\s*(?:по|до|-|–)\s*(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"{match.Groups[1].Value}–{match.Groups[2].Value}";
        }

        var doMatch = Regex.Match(timePart, @"^(?:до|по)\s*(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
        if (doMatch.Success)
        {
            return $"до {doMatch.Groups[1].Value}";
        }

        var zMatch = Regex.Match(timePart, @"^з\s*(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
        if (zMatch.Success)
        {
            return $"з {zMatch.Groups[1].Value}";
        }

        return timePart.Trim();
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

        string formattedDate = EditorialContentTransformer.FormatDate(editionDate);

        var sb = new StringBuilder();
        sb.AppendLine($"АВАРІЙНІ ЗНЕСТРУМЛЕННЯ — {formattedDate}");
        sb.AppendLine();
        sb.AppendLine("Старокостянтинівська міська територіальна громада");
        sb.AppendLine();

        // 1. City of Starokostiantyniv first (top priority)
        var city = emergencyTerritories.FirstOrDefault(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase));
        if (city != null)
        {
            string cityText = RenderCitySection(city.EmergencyRecords);
            if (!string.IsNullOrWhiteSpace(cityText))
            {
                sb.AppendLine(cityText);
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
            var ruralBlocks = new List<string>();
            foreach (var district in ruralDistricts)
            {
                string distText = RenderDistrictSection(district, isEmergency: true);
                if (!string.IsNullOrWhiteSpace(distText))
                {
                    ruralBlocks.Add(distText);
                }
            }

            if (ruralBlocks.Count > 0)
            {
                sb.AppendLine("СТАРОСТИНСЬКІ ОКРУГИ");
                sb.AppendLine();
                foreach (var block in ruralBlocks)
                {
                    sb.AppendLine(block);
                    sb.AppendLine();
                }
            }
        }

        // Technical footer
        var localTime = (lastUpdatedUtc ?? DateTime.UtcNow).AddHours(3);
        sb.AppendLine("Технічна інформація: ");
        sb.AppendLine($"Останнє оновлення журналу: {localTime:HH:mm}");
        sb.AppendLine("Стан моніторингу: активний  ");
        sb.AppendLine();
        sb.AppendLine("#аварійнівідключення #відключення #Старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd().Replace("\r\n", "\n");
    }

    public static string FormatFacebookPlannedPost(
        string editionDate,
        IReadOnlyList<AggregatedTerritoryData> territories,
        EditorialContentTransformer? transformer = null,
        DateTime? lastUpdatedUtc = null)
    {
        string formattedDate = EditorialContentTransformer.FormatDate(editionDate);

        var sb = new StringBuilder();
        sb.AppendLine($"ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — {formattedDate}");
        sb.AppendLine();
        sb.AppendLine("Старокостянтинівська міська територіальна громада");
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
                string cityText = RenderCitySection(city.PlannedRecords);
                if (!string.IsNullOrWhiteSpace(cityText))
                {
                    sb.AppendLine(cityText);
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
                var ruralBlocks = new List<string>();
                foreach (var district in ruralDistricts)
                {
                    string distText = RenderDistrictSection(district, isEmergency: false);
                    if (!string.IsNullOrWhiteSpace(distText))
                    {
                        ruralBlocks.Add(distText);
                    }
                }

                if (ruralBlocks.Count > 0)
                {
                    sb.AppendLine("СТАРОСТИНСЬКІ ОКРУГИ");
                    sb.AppendLine();
                    foreach (var block in ruralBlocks)
                    {
                        sb.AppendLine(block);
                        sb.AppendLine();
                    }
                }
            }
        }

        // Technical footer
        var localTime = (lastUpdatedUtc ?? DateTime.UtcNow).AddHours(3);
        sb.AppendLine("Технічна інформація: ");
        sb.AppendLine($"Останнє оновлення журналу: {localTime:HH:mm}");
        sb.AppendLine("Стан моніторингу: активний  ");
        sb.AppendLine();
        sb.AppendLine("#відключення #плановівідключення #Старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd().Replace("\r\n", "\n");
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

        string formattedDate = EditorialContentTransformer.FormatDate(tomorrowDate);

        var sb = new StringBuilder();
        sb.AppendLine("ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА");
        sb.AppendLine(formattedDate);
        sb.AppendLine();
        sb.AppendLine("Старокостянтинівська міська територіальна громада");
        sb.AppendLine();

        // 1. City first
        var city = territories.FirstOrDefault(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) &&
            ((t.PlannedRecords != null && t.PlannedRecords.Count > 0) || (t.EmergencyRecords != null && t.EmergencyRecords.Count > 0)));
        if (city != null)
        {
            var combinedRecords = (city.PlannedRecords ?? Array.Empty<OutageRecord>())
                .Concat(city.EmergencyRecords ?? Array.Empty<OutageRecord>())
                .ToList();

            string cityText = RenderCitySection(combinedRecords);
            if (!string.IsNullOrWhiteSpace(cityText))
            {
                sb.AppendLine(cityText);
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
            var ruralBlocks = new List<string>();
            foreach (var district in ruralDistricts)
            {
                string distText = RenderDistrictSection(district, isEmergency: false);
                if (!string.IsNullOrWhiteSpace(distText))
                {
                    ruralBlocks.Add(distText);
                }
            }

            if (ruralBlocks.Count > 0)
            {
                sb.AppendLine("СТАРОСТИНСЬКІ ОКРУГИ");
                sb.AppendLine();
                foreach (var block in ruralBlocks)
                {
                    sb.AppendLine(block);
                    sb.AppendLine();
                }
            }
        }

        sb.AppendLine("Інформація може змінюватися відповідно до поточних розпоряджень НЕК «Укренерго».");
        sb.AppendLine();

        // Technical footer
        var localTime = (lastUpdatedUtc ?? DateTime.UtcNow).AddHours(3);
        sb.AppendLine("Технічна інформація: ");
        sb.AppendLine($"Останнє оновлення журналу: {localTime:HH:mm}");
        sb.AppendLine("Стан моніторингу: активний  ");
        sb.AppendLine();
        sb.AppendLine("#прогноз #відключення #Старокостянтинів #громада #svitlosk");

        return sb.ToString().TrimEnd().Replace("\r\n", "\n");
    }

    private static string RenderCitySection(IReadOnlyList<OutageRecord> records)
    {
        var blocks = ParseFacebookBlocks(records, isCity: true, canonicalName: "Місто Старокостянтинів");
        if (blocks.Count == 0 || !blocks.Any(b => b.Streets.Count > 0))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("МІСТО СТАРОКОСТЯНТИНІВ");

        var distinctIntervals = blocks
            .Select(b => b.TimeInterval)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasCommonInterval = distinctIntervals.Count == 1;

        if (hasCommonInterval)
        {
            sb.AppendLine(distinctIntervals[0]);
            sb.AppendLine();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Streets.Count == 0) continue;

                if (!string.IsNullOrEmpty(b.Subqueue))
                {
                    sb.AppendLine($"Черга {b.Subqueue}");
                }

                foreach (var st in b.Streets)
                {
                    sb.AppendLine($"• {st}");
                }

                if (i < blocks.Count - 1 && !string.IsNullOrEmpty(b.Subqueue))
                {
                    sb.AppendLine();
                }
            }
        }
        else
        {
            sb.AppendLine();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Streets.Count == 0) continue;

                string header = !string.IsNullOrEmpty(b.Subqueue) && !string.IsNullOrEmpty(b.TimeInterval)
                    ? $"{b.TimeInterval} (Черга {b.Subqueue})"
                    : !string.IsNullOrEmpty(b.Subqueue)
                        ? $"Черга {b.Subqueue}"
                        : b.TimeInterval;

                if (!string.IsNullOrEmpty(header))
                {
                    sb.AppendLine(header);
                }

                foreach (var st in b.Streets)
                {
                    sb.AppendLine($"• {st}");
                }

                if (i < blocks.Count - 1)
                {
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string RenderDistrictSection(AggregatedTerritoryData district, bool isEmergency)
    {
        var records = isEmergency
            ? district.EmergencyRecords
            : (district.PlannedRecords != null && district.PlannedRecords.Count > 0 ? district.PlannedRecords : district.EmergencyRecords);

        if (records == null || records.Count == 0) return string.Empty;

        var blocks = ParseFacebookBlocks(records, isCity: false, canonicalName: district.CanonicalName);
        if (blocks.Count == 0 || !blocks.Any(b => b.Streets.Count > 0))
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine(district.CanonicalName.ToUpperInvariant());

        var distinctIntervals = blocks
            .Select(b => b.TimeInterval)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        bool hasCommonInterval = distinctIntervals.Count == 1;

        if (hasCommonInterval)
        {
            sb.AppendLine(distinctIntervals[0]);
            sb.AppendLine();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Streets.Count == 0) continue;

                if (!string.IsNullOrEmpty(b.Settlement))
                {
                    sb.AppendLine(b.Settlement);
                }

                foreach (var st in b.Streets)
                {
                    sb.AppendLine($"• {st}");
                }

                if (i < blocks.Count - 1)
                {
                    sb.AppendLine();
                }
            }
        }
        else
        {
            sb.AppendLine();

            for (int i = 0; i < blocks.Count; i++)
            {
                var b = blocks[i];
                if (b.Streets.Count == 0) continue;

                if (!string.IsNullOrEmpty(b.Settlement))
                {
                    string settHeader = !string.IsNullOrEmpty(b.TimeInterval)
                        ? $"{b.Settlement} ({b.TimeInterval})"
                        : b.Settlement;
                    sb.AppendLine(settHeader);
                }
                else if (!string.IsNullOrEmpty(b.TimeInterval))
                {
                    sb.AppendLine(b.TimeInterval);
                }

                foreach (var st in b.Streets)
                {
                    sb.AppendLine($"• {st}");
                }

                if (i < blocks.Count - 1)
                {
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private class FacebookBlock
    {
        public string Settlement { get; set; } = string.Empty;
        public string TimeInterval { get; set; } = string.Empty;
        public string? Subqueue { get; set; }
        public List<string> Streets { get; } = new();
    }

    private static List<FacebookBlock> ParseFacebookBlocks(IReadOnlyList<OutageRecord> records, bool isCity, string canonicalName)
    {
        var blocks = new List<FacebookBlock>();
        FacebookBlock? currentBlock = null;

        foreach (var rec in records)
        {
            if (string.IsNullOrWhiteSpace(rec.Details)) continue;
            string details = rec.Details.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = details.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.Contains("не зафіксовано", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("не передбачено", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("не заплановано", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Skip pure section headers if passed in Details
                if (line.StartsWith("---") || line.StartsWith("===") || line.StartsWith("КІНЕЦЬ") ||
                    line.StartsWith("Кількість") || line.StartsWith("ДАНІ") || line.StartsWith("Дата:") ||
                    line.StartsWith("Джерело:") || line.StartsWith("Останнє") || line.StartsWith("Перша") ||
                    line.StartsWith("Статус:") || line.StartsWith("Хеш") || line.StartsWith("Історія"))
                {
                    continue;
                }

                // 1. Settlement with interval e.g. "м. Старокостянтинів | з 09:00 до 20:00" or "с. Зеленці | з 09:00 до 17:00"
                var settPipeMatch = Regex.Match(line, @"^((?:с\.|м\.|селище)\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s*\|\s*(.*)$", RegexOptions.IgnoreCase);
                if (settPipeMatch.Success)
                {
                    string sett = settPipeMatch.Groups[1].Value.Trim();
                    string timePart = settPipeMatch.Groups[2].Value.Trim();
                    string interval = FormatOutageTimeRange(timePart);

                    currentBlock = new FacebookBlock
                    {
                        Settlement = isCity ? string.Empty : sett,
                        TimeInterval = interval
                    };
                    blocks.Add(currentBlock);
                    continue;
                }

                // 2. Standalone settlement e.g. "с. Самчики" or "с. Зеленці"
                var standAloneSettMatch = Regex.Match(line, @"^(?:с\.|селище)\s+[А-Яа-яA-Za-zіІїЇєЄґҐ'\-]+(?:\s+[А-Яа-яA-Za-zіІїЇєЄґҐ'\-]+)?$", RegexOptions.IgnoreCase);
                if (standAloneSettMatch.Success && !line.Contains("вул.", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentBlock != null && string.IsNullOrEmpty(currentBlock.Settlement) && currentBlock.Streets.Count == 0)
                    {
                        currentBlock.Settlement = line;
                    }
                    else
                    {
                        currentBlock = new FacebookBlock { Settlement = line };
                        blocks.Add(currentBlock);
                    }
                    continue;
                }

                // 3. Pure time line e.g. "09:00 - 17:00" or "з 08:00 по 12:00 1 черга" or "до 16:00"
                var timeLineMatch = Regex.Match(line, @"^(?:з\s*)?(\d{1,2}:\d{2})\s*(?:по|до|-|–)\s*(\d{1,2}:\d{2})(?:\s+(.*))?$", RegexOptions.IgnoreCase);
                if (timeLineMatch.Success && !line.Contains("вул.", StringComparison.OrdinalIgnoreCase))
                {
                    string interval = $"{timeLineMatch.Groups[1].Value}–{timeLineMatch.Groups[2].Value}";
                    string remainder = timeLineMatch.Groups[3].Value.Trim();
                    string? q = null;
                    var qm = Regex.Match(remainder, @"(\d+(\.\d+)?)\s*черг[аи]", RegexOptions.IgnoreCase);
                    if (qm.Success) q = qm.Groups[1].Value;

                    if (currentBlock != null && string.IsNullOrEmpty(currentBlock.TimeInterval) && currentBlock.Streets.Count == 0)
                    {
                        currentBlock.TimeInterval = interval;
                        if (q != null) currentBlock.Subqueue = q;
                    }
                    else
                    {
                        currentBlock = new FacebookBlock { TimeInterval = interval, Subqueue = q };
                        blocks.Add(currentBlock);
                    }
                    continue;
                }

                var doLineMatch = Regex.Match(line, @"^(?:до|по)\s*(\d{1,2}:\d{2})(?:\s+(.*))?$", RegexOptions.IgnoreCase);
                if (doLineMatch.Success && !line.Contains("вул.", StringComparison.OrdinalIgnoreCase))
                {
                    string interval = $"до {doLineMatch.Groups[1].Value}";
                    if (currentBlock != null && string.IsNullOrEmpty(currentBlock.TimeInterval) && currentBlock.Streets.Count == 0)
                    {
                        currentBlock.TimeInterval = interval;
                    }
                    else
                    {
                        currentBlock = new FacebookBlock { TimeInterval = interval };
                        blocks.Add(currentBlock);
                    }
                    continue;
                }

                // 4. Subqueue header e.g. "Черга 1.1" or "1 черга"
                var queueMatch = Regex.Match(line, @"^(?:Черга|Підчерга)\s*(\d+(\.\d+)?)", RegexOptions.IgnoreCase);
                if (queueMatch.Success)
                {
                    string q = queueMatch.Groups[1].Value;
                    if (currentBlock != null && string.IsNullOrEmpty(currentBlock.Subqueue) && currentBlock.Streets.Count == 0)
                    {
                        currentBlock.Subqueue = q;
                    }
                    else
                    {
                        currentBlock = new FacebookBlock { Subqueue = q };
                        blocks.Add(currentBlock);
                    }
                    continue;
                }

                // 5. Line with embedded time e.g. "вул. Миру... Час: 10:00 - 14:00. Причина: ..."
                string addressCandidate = line;
                var inlineTime = Regex.Match(addressCandidate, @"Час:\s*(?:з\s*)?(\d{1,2}:\d{2})\s*(?:по|до|-|–)\s*(\d{1,2}:\d{2})", RegexOptions.IgnoreCase);
                if (inlineTime.Success)
                {
                    string interval = $"{inlineTime.Groups[1].Value}–{inlineTime.Groups[2].Value}";
                    if (currentBlock == null)
                    {
                        currentBlock = new FacebookBlock { TimeInterval = interval };
                        blocks.Add(currentBlock);
                    }
                    else if (string.IsNullOrEmpty(currentBlock.TimeInterval))
                    {
                        currentBlock.TimeInterval = interval;
                    }
                    addressCandidate = addressCandidate.Replace(inlineTime.Value, "").Trim();
                }

                // Remove "Причина: ..."
                addressCandidate = Regex.Replace(addressCandidate, @"Причина:.*$", "", RegexOptions.IgnoreCase).Trim();
                // Clean queues inside address text
                addressCandidate = Regex.Replace(addressCandidate, @"[⚡📋]?\s*черг[аи]\s*:\s*\d+(\.\d+)?", "", RegexOptions.IgnoreCase);
                addressCandidate = Regex.Replace(addressCandidate, @"\d+(\.\d+)?\s*черг[аи]", "", RegexOptions.IgnoreCase);

                if (string.IsNullOrWhiteSpace(addressCandidate)) continue;

                string formattedAddr = FormatAddressLine(addressCandidate);
                string compacted = TerritoryAggregator.CompactHouseNumbers(formattedAddr);
                if (!string.IsNullOrWhiteSpace(compacted))
                {
                    if (currentBlock == null)
                    {
                        currentBlock = new FacebookBlock();
                        blocks.Add(currentBlock);
                    }
                    currentBlock.Streets.Add(compacted);
                }
            }
        }

        return blocks;
    }

    private static string FormatAddressLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return string.Empty;
        string clean = StripHtml(line);
        // Remove leading bullets/dashes
        clean = Regex.Replace(clean, @"^[•\-\*\s]+", "").Trim();
        // Format "вул. Назва: буд." -> "вул. Назва, буд."
        clean = Regex.Replace(clean, @":(?!\d{2})", ",");
        clean = Regex.Replace(clean, @"(вул\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1, $2");
        clean = Regex.Replace(clean, @"(пров\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1, $2");
        clean = Regex.Replace(clean, @"(с\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(вул\.)", "$1, $2");
        clean = Regex.Replace(clean, @",\s*,", ",");
        clean = Regex.Replace(clean, @"\s+", " ");
        return clean.Trim(' ', ',').Trim();
    }
}
