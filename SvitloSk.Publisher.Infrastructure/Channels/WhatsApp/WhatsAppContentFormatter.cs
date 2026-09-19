using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

/// <summary>
/// Formats publication content specifically for the WhatsApp Messenger Channel:
/// - Strictly adheres to WhatsApp Markdown (*bold*, > quote, `code`, - list, _italic_).
/// - Enforces the approved visual standard: *м. СТАРОКОСТЯНТИНІВ* for the administrative center.
/// - Renders Emergency outages as high-contrast callout quotes (> ).
/// - Formats time intervals into digital monospace badges (`HH:mm – HH:mm`).
/// - 100% free of HTML tags and decorative emojis.
/// </summary>
public static class WhatsAppContentFormatter
{
    public const int MaxTextMessageLength = 4096;
    public const int SafeMessageChunkLimit = 3800;

    private static readonly string[] UkrainianMonthGenitive = 
    {
        "", "січня", "лютого", "березня", "квітня", "травня", "червня",
        "липня", "серпня", "вересня", "жовтня", "листопада", "грудня"
    };

    public static string FormatFullUkrainianDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return dateStr;

        if (DateTime.TryParse(dateStr, out var dt) ||
            DateTime.TryParseExact(dateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt) ||
            DateTime.TryParseExact(dateStr, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
        {
            string day = dt.Day.ToString();
            string month = dt.Month >= 1 && dt.Month <= 12 ? UkrainianMonthGenitive[dt.Month] : dt.Month.ToString();
            string year = dt.Year.ToString();
            string dayOfWeek = EditorialContentTransformer.GetUkrainianDayOfWeek(dt.DayOfWeek).ToLowerInvariant();

            return $"{day} {month} {year} року, {dayOfWeek}";
        }

        return dateStr;
    }

    public static string FormatShortDate(string dateStr)
    {
        return EditorialContentTransformer.FormatDate(dateStr);
    }

    public static string RenderJournalHeader(string rawDate, JournalSummaryStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*");

        string fullDate = FormatFullUkrainianDate(rawDate);
        if (!string.IsNullOrWhiteSpace(fullDate))
        {
            sb.AppendLine($"*{fullDate}*");
        }

        sb.AppendLine();

        if (stats.PlannedSettlements != null && stats.PlannedSettlements.Count > 0)
        {
            string pList = string.Join(", ", stats.PlannedSettlements);
            sb.AppendLine($"*Планові знеструмлення:* {pList}");
        }
        else
        {
            sb.AppendLine("*Планові знеструмлення:* відсутні");
        }

        if (stats.EmergencySettlements != null && stats.EmergencySettlements.Count > 0)
        {
            string eList = string.Join(", ", stats.EmergencySettlements);
            sb.AppendLine($"*Аварійні знеструмлення:* {eList}");
        }
        else
        {
            sb.AppendLine("*Аварійні знеструмлення:* відсутні");
        }

        sb.AppendLine();
        sb.AppendLine("_Інформація оновлюється автоматично протягом доби_");

        return CleanOutput(sb.ToString());
    }

    public static string RenderNoOutagesPost(string rawDate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("*ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МТГ*");

        string fullDate = FormatFullUkrainianDate(rawDate);
        if (!string.IsNullOrWhiteSpace(fullDate))
        {
            sb.AppendLine($"*{fullDate}*");
        }

        sb.AppendLine();
        sb.AppendLine("*Планові знеструмлення:* відсутні");
        sb.AppendLine("*Аварійні знеструмлення:* відсутні");
        sb.AppendLine();
        sb.AppendLine("_Інформація оновлюється автоматично протягом доби_");

        return CleanOutput(sb.ToString());
    }

    public static string RenderSystemStatus(DateTime utcTime, string monitoringState = "активний")
    {
        var localTime = utcTime.AddHours(3);
        var sb = new StringBuilder();
        sb.AppendLine($"Останнє оновлення журналу: *{localTime:HH:mm}*");
        sb.AppendLine($"Стан моніторингу: {monitoringState}");
        return CleanOutput(sb.ToString());
    }

    public static string RenderTerritoryPost(AggregatedTerritoryData data, bool isTomorrow = false, string? tomorrowDate = null)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        var sb = new StringBuilder();

        // 1. Forecast Header (if tomorrow)
        if (isTomorrow)
        {
            string tomShortDate = !string.IsNullOrWhiteSpace(tomorrowDate) ? FormatShortDate(tomorrowDate) : "завтра";
            sb.AppendLine($"*ПРОГНОЗ НА ЗАВТРА* • *{tomShortDate}*");
        }

        // 2. Territory Title
        string territoryTitle;
        if (data.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) ||
            data.CanonicalName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase))
        {
            territoryTitle = "*м. СТАРОКОСТЯНТИНІВ*";
        }
        else
        {
            territoryTitle = $"*{data.CanonicalName.ToUpperInvariant()}*";
        }
        sb.AppendLine(territoryTitle);
        sb.AppendLine();

        // 3. Emergency Block (Rendered with > quote callouts)
        if (data.EmergencyRecords != null && data.EmergencyRecords.Count > 0)
        {
            var emergBlocks = SplitIntoIntervalBlocks(data.EmergencyRecords);
            bool hasMultipleEmergBlocks = emergBlocks.Count > 1;

            for (int i = 0; i < emergBlocks.Count; i++)
            {
                var block = emergBlocks[i];
                string timeBadge = !string.IsNullOrEmpty(block.TimeInterval) ? $" • *{block.TimeInterval}*" : string.Empty;
                sb.AppendLine($"> *АВАРІЙНІ ЗНЕСТРУМЛЕННЯ*{timeBadge}");

                string body = RenderSubBlockDetails(block.Lines, block.TimeInterval, data.CanonicalName, isEmergency: true, showVillageIntervals: hasMultipleEmergBlocks);
                if (!string.IsNullOrWhiteSpace(body))
                {
                    sb.AppendLine(body);
                }
                sb.AppendLine();
            }
        }

        // 4. Planned Block
        if (data.PlannedRecords != null && data.PlannedRecords.Count > 0)
        {
            var planBlocks = SplitIntoIntervalBlocks(data.PlannedRecords);
            bool hasMultipleVillagesOrBlocks = planBlocks.Count > 1 || planBlocks.Any(b => b.Lines.Any(l => Regex.IsMatch(l, @"^с\.", RegexOptions.IgnoreCase)));

            if (hasMultipleVillagesOrBlocks && planBlocks.Count > 1)
            {
                // Multi-village district with distinct intervals: main header has no time badge, each village has its own badge
                sb.AppendLine("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*");
                sb.AppendLine();

                for (int i = 0; i < planBlocks.Count; i++)
                {
                    var block = planBlocks[i];
                    if (i > 0)
                    {
                        sb.AppendLine();
                    }
                    string body = RenderSubBlockDetails(block.Lines, block.TimeInterval, data.CanonicalName, isEmergency: false, showVillageIntervals: true);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                }
            }
            else
            {
                // Single block (e.g. City or single village): header gets the time badge
                for (int i = 0; i < planBlocks.Count; i++)
                {
                    var block = planBlocks[i];
                    string timeBadge = !string.IsNullOrEmpty(block.TimeInterval) ? $" • *{block.TimeInterval}*" : string.Empty;

                    if (i > 0)
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine($"*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*{timeBadge}");
                    sb.AppendLine();

                    string body = RenderSubBlockDetails(block.Lines, block.TimeInterval, data.CanonicalName, isEmergency: false, showVillageIntervals: false);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                }
            }
        }

        return CleanOutput(sb.ToString());
    }

    private class IntervalSubBlock
    {
        public string TimeInterval { get; set; } = string.Empty;
        public List<string> Lines { get; set; } = new();
    }

    private static List<IntervalSubBlock> SplitIntoIntervalBlocks(IReadOnlyList<OutageRecord> records)
    {
        var result = new List<IntervalSubBlock>();
        IntervalSubBlock? currentBlock = null;

        foreach (var rec in records)
        {
            if (string.IsNullOrWhiteSpace(rec.Details)) continue;
            var lines = rec.Details.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var settlementMatch = Regex.Match(line, @"^(?:с\.|м\.|селище).*?\|\s*(.*)$", RegexOptions.IgnoreCase);
                if (settlementMatch.Success)
                {
                    string interval = FormatTimeIntervalToShortRange(settlementMatch.Groups[1].Value.Trim());
                    currentBlock = new IntervalSubBlock { TimeInterval = interval };
                    currentBlock.Lines.Add(line);
                    result.Add(currentBlock);
                }
                else
                {
                    if (currentBlock == null)
                    {
                        var timeMatch = Regex.Match(line.Trim(), @"^(?:з\s*)?(\d{2}:\d{2})\s*(?:по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
                        string interval = timeMatch.Success ? $"{timeMatch.Groups[1].Value} – {timeMatch.Groups[2].Value}" : string.Empty;
                        currentBlock = new IntervalSubBlock { TimeInterval = interval };
                        result.Add(currentBlock);
                    }
                    currentBlock.Lines.Add(line);
                }
            }
        }

        if (result.Count == 0 && records.Count > 0)
        {
            result.Add(new IntervalSubBlock());
        }

        return result;
    }

    private static string RenderSubBlockDetails(IEnumerable<string> lines, string? commonTimeInterval, string? canonicalTerritoryName, bool isEmergency, bool showVillageIntervals = false)
    {
        var sb = new StringBuilder();
        string prefix = isEmergency ? "> " : string.Empty;

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Strip queues and emojis from feed
            line = StripEmojisAndQueues(line);
            line = Regex.Replace(line, @"^[•\-\*\s]+", "").Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Settlement header e.g. "с. Пашківці | з 08:30 до 16:30"
            var settlementMatch = Regex.Match(line, @"^((?:с\.|м\.|селище)\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s*\|\s*(.*)$", RegexOptions.IgnoreCase);
            if (settlementMatch.Success)
            {
                string settlement = settlementMatch.Groups[1].Value.Trim();
                string timePart = settlementMatch.Groups[2].Value.Trim();
                string interval = FormatTimeIntervalToShortRange(timePart);

                bool isRedundantCityHeader = !string.IsNullOrEmpty(canonicalTerritoryName) &&
                    (canonicalTerritoryName.Contains(settlement, StringComparison.OrdinalIgnoreCase) ||
                     settlement.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) && canonicalTerritoryName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase));

                if (!isRedundantCityHeader)
                {
                    string effectiveInterval = !string.IsNullOrEmpty(interval) ? interval : commonTimeInterval ?? string.Empty;
                    string badge = (showVillageIntervals && !string.IsNullOrEmpty(effectiveInterval)) || 
                                   (!string.IsNullOrEmpty(interval) && !interval.Equals(commonTimeInterval, StringComparison.OrdinalIgnoreCase))
                        ? $" • *{effectiveInterval}*"
                        : string.Empty;

                    sb.AppendLine($"{prefix}*{settlement}*{badge}");
                }
                continue;
            }

            // Standalone time line
            var timeMatch = Regex.Match(line, @"^(з\s*)?(\d{2}:\d{2})\s*(по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
            if (timeMatch.Success)
            {
                string interval = $"{timeMatch.Groups[2].Value} – {timeMatch.Groups[4].Value}";
                sb.AppendLine($"{prefix}*{interval}*");

                string remainder = line.Substring(timeMatch.Length).Trim(' ', ':', ',', '-').Trim();
                if (!string.IsNullOrEmpty(remainder))
                {
                    string compacted = TerritoryAggregator.CompactHouseNumbers(FormatAddressLine(remainder));
                    sb.AppendLine($"{prefix}- {compacted}");
                }
            }
            else
            {
                if (line.Contains("не зафіксовано", StringComparison.OrdinalIgnoreCase) || line.Contains("не заплановано", StringComparison.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{prefix}{line}");
                }
                else
                {
                    string compacted = TerritoryAggregator.CompactHouseNumbers(FormatAddressLine(line));
                    sb.AppendLine($"{prefix}- {compacted}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatTimeIntervalToShortRange(string timePart)
    {
        if (string.IsNullOrWhiteSpace(timePart)) return string.Empty;
        var match = Regex.Match(timePart, @"(\d{2}:\d{2})\s*(?:по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"{match.Groups[1].Value} – {match.Groups[2].Value}";
        }
        return timePart;
    }

    public static string FormatAddressLine(string line)
    {
        string formatted = Regex.Replace(line, @":(?!\d{2})", ": ");
        formatted = Regex.Replace(formatted, @"(вул\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1: $2");
        formatted = Regex.Replace(formatted, @"(пров\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1: $2");
        formatted = Regex.Replace(formatted, @"(с\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(вул\.)", "$1, $2");
        formatted = Regex.Replace(formatted, @",\s*,", ",");
        formatted = Regex.Replace(formatted, @"\s+", " ");
        return formatted.Trim(' ', ',').Trim();
    }

    public static string ConvertToWhatsAppMarkdown(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string result = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Normalize city header to approved standard: *м. СТАРОКОСТЯНТИНІВ*
        result = Regex.Replace(result, @"<b>(?:Місто\s+Старокостянтинів|Старокостянтинів)</b>", "*м. СТАРОКОСТЯНТИНІВ*", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"^\*?(?:Місто\s+Старокостянтинів|Старокостянтинів)\*?$", "*м. СТАРОКОСТЯНТИНІВ*", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        // Collapse multiline header labels into a single line: "Аварійні знеструмлення:\nм. Старокостянтинів" -> "Аварійні знеструмлення: м. Старокостянтинів"
        result = Regex.Replace(result, @"(\*?(?:<b>)?(?:Планові|Аварійні)\s+знеструмлення:(?:</b>)?\*?)\s*\n\s*([^\n]+)", "$1 $2", RegexOptions.IgnoreCase);

        // Convert blockquotes to WhatsApp > quotes
        result = Regex.Replace(result, @"<blockquote>([\s\S]*?)</blockquote>", m =>
        {
            var inner = m.Groups[1].Value.Trim();
            var lines = inner.Split('\n');
            var sbQuote = new StringBuilder();
            foreach (var l in lines)
            {
                string cleanLine = l.Trim();
                if (!string.IsNullOrEmpty(cleanLine))
                {
                    // Clean inner bold tags
                    cleanLine = Regex.Replace(cleanLine, @"<b>(.*?)</b>", "*$1*");
                    sbQuote.AppendLine($"> {cleanLine}");
                }
            }
            return sbQuote.ToString().TrimEnd();
        });

        // Convert HTML tags to WhatsApp Markdown
        result = Regex.Replace(result, @"<b>(.*?)</b>", "*$1*");
        result = Regex.Replace(result, @"<i>(.*?)</i>", "_$1_");
        result = Regex.Replace(result, @"<code>(.*?)</code>", "*$1*");

        // Convert (HH:mm–HH:mm) into unified bold badges • *HH:mm – HH:mm*
        result = Regex.Replace(result, @"\(\s*(\d{2}:\d{2})\s*(?:–|-|до|по)\s*(\d{2}:\d{2})\s*\)", "• *$1 – $2*");

        // Unify time formatting: strip backticks around times and time ranges to keep font size and appearance unified
        result = Regex.Replace(result, @"`(\d{2}:\d{2}(?:\s*(?:–|-|до|по)\s*\d{2}:\d{2})?)`", "*$1*");
        result = Regex.Replace(result, @"`([^`\n]+)`", "*$1*");

        // Unify system status: "Останнє оновлення: 17:31" -> "Останнє оновлення журналу: *17:31*"
        result = Regex.Replace(result, @"\*?Останнє оновлення(?:\s+журналу)?\*?:\s*\*?(\d{2}:\d{2})\*?", "Останнє оновлення журналу: *$1*", RegexOptions.IgnoreCase);

        // Strip remaining HTML tags
        result = Regex.Replace(result, @"</?[a-zA-Z0-9]+[^>]*>", "");

        // Strip emojis and queues
        result = StripEmojisAndQueues(result);

        return CleanOutput(result);
    }

    public static string StripEmojisAndQueues(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string clean = Regex.Replace(text, @"[⚡📋💡📍🚨ℹ️🔮]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"[⚡📋]?\s*черг[аи]\s*:\s*\d+(\.\d+)?", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\d+(\.\d+)?\s*черг[аи]", "", RegexOptions.IgnoreCase);
        return clean.Trim();
    }

    public static List<string> SplitMessage(string text, int limit = SafeMessageChunkLimit)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;
        if (text.Length <= limit)
        {
            result.Add(text);
            return result;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var currentChunk = new StringBuilder();

        foreach (var line in lines)
        {
            if (currentChunk.Length + line.Length + 1 > limit)
            {
                if (currentChunk.Length > 0)
                {
                    result.Add(currentChunk.ToString().TrimEnd());
                    currentChunk.Clear();
                }

                if (line.Length > limit)
                {
                    string longLine = line;
                    while (longLine.Length > limit)
                    {
                        result.Add(longLine.Substring(0, limit));
                        longLine = longLine.Substring(limit);
                    }
                    currentChunk.Append(longLine);
                }
                else
                {
                    currentChunk.Append(line).Append('\n');
                }
            }
            else
            {
                currentChunk.Append(line).Append('\n');
            }
        }

        if (currentChunk.Length > 0)
        {
            result.Add(currentChunk.ToString().TrimEnd());
        }

        return result;
    }

    private static string CleanOutput(string output)
    {
        string result = output.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
        // Final safety check against accidental HTML tags
        result = Regex.Replace(result, @"</?(?:b|i|blockquote|code|pre|br|p|div|span)[^>]*>", "", RegexOptions.IgnoreCase);
        // Final safety check against accidental emojis
        result = Regex.Replace(result, @"[⚡📋💡📍🚨ℹ️🔮]", "");
        return result;
    }
}
