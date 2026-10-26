using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using SvitloSk.Publisher.Core.Domain;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

public record ParsedWhatsAppMarker(
    string Marker,
    char Type,
    string DateDdMm,
    int? SequenceNumber
);

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

    /// <summary>
    /// Formats a date string (yyyy-MM-dd or dd.MM.yyyy) into short marker date "dd.MM".
    /// </summary>
    public static string FormatMarkerDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return dateStr;

        if (DateTime.TryParseExact(dateStr, new[] { "yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dt) ||
            DateTime.TryParse(dateStr, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
        {
            return dt.ToString("dd.MM");
        }

        var match = Regex.Match(dateStr, @"(\d{2})\.(\d{2})");
        if (match.Success)
        {
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}";
        }

        return dateStr;
    }

    /// <summary>
    /// Builds a plain-text WhatsApp reconciliation marker:
    /// - Type 'T' or 'F': "{type} · dd.MM · NN" (e.g., "T · 26.10 · 01")
    /// - Type 'G' or 'S': "{type} · dd.MM"      (e.g., "S · 26.10")
    /// </summary>
    public static string FormatMarker(char type, string dateStr, int? sequenceNumber = null)
    {
        char upperType = char.ToUpperInvariant(type);
        string ddMm = FormatMarkerDate(dateStr);
        if ((upperType == 'T' || upperType == 'F') && sequenceNumber.HasValue)
        {
            return $"{upperType} · {ddMm} · {sequenceNumber.Value:D2}";
        }

        return $"{upperType} · {ddMm}";
    }

    /// <summary>
    /// Appends the plain-text marker as the final line of a WhatsApp post (replacing any previous trailing marker).
    /// </summary>
    public static string AppendMarker(string content, string marker)
    {
        if (string.IsNullOrWhiteSpace(marker)) return content ?? string.Empty;
        string body = StripMarker(content ?? string.Empty);
        if (string.IsNullOrWhiteSpace(body))
        {
            return marker.Trim();
        }

        return $"{body.TrimEnd()}\n\n{marker.Trim()}";
    }

    /// <summary>
    /// Strips any trailing WhatsApp marker line from the post text for clean content hashing/comparison.
    /// </summary>
    public static string StripMarker(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return string.Empty;
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n').ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count > 0 && TryParseMarkerLine(lines[^1], out _))
        {
            lines.RemoveAt(lines.Count - 1);
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            {
                lines.RemoveAt(lines.Count - 1);
            }
        }

        return string.Join("\n", lines);
    }

    public static bool TryParseMarkerLine(string? line, out ParsedWhatsAppMarker? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        string clean = line.Trim().Trim('_', '*', '`').Trim();
        var tfMatch = Regex.Match(clean, @"^([TF])\s*[·•]\s*(\d{2}\.\d{2})\s*[·•]\s*(\d{2})$");
        if (tfMatch.Success)
        {
            char t = tfMatch.Groups[1].Value[0];
            string d = tfMatch.Groups[2].Value;
            int seq = int.Parse(tfMatch.Groups[3].Value);
            parsed = new ParsedWhatsAppMarker($"{t} · {d} · {seq:D2}", t, d, seq);
            return true;
        }

        var gsMatch = Regex.Match(clean, @"^([GS])\s*[·•]\s*(\d{2}\.\d{2})$");
        if (gsMatch.Success)
        {
            char t = gsMatch.Groups[1].Value[0];
            string d = gsMatch.Groups[2].Value;
            parsed = new ParsedWhatsAppMarker($"{t} · {d}", t, d, null);
            return true;
        }

        return false;
    }

    public static bool TryExtractMarkerFromText(string? fullText, out ParsedWhatsAppMarker? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(fullText)) return false;

        var lines = fullText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            if (TryParseMarkerLine(lines[i], out parsed))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves a canonical TerritoryId from the first line / title of a WhatsApp territory post
    /// (enables self-healing mapping between sequence numbers NN and territories directly from channel DOM).
    /// </summary>
    public static string? ResolveTerritoryIdFromPostText(string? postText, bool isTomorrow = false)
    {
        if (string.IsNullOrWhiteSpace(postText)) return null;

        var lines = postText.Replace("\r\n", "\n").Replace("\r", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0) return null;

        if (lines[0].Contains("ЖУРНАЛ ЗНЕСТРУМЛЕНЬ", StringComparison.OrdinalIgnoreCase))
        {
            return isTomorrow ? "tomorrow_separator" : "journal_header";
        }

        if (lines[0].Contains("ГРАФІК ПОГОДИННИХ", StringComparison.OrdinalIgnoreCase) ||
            lines[0].Contains("ГРАФІК ЗНЕСТРУМЛЕНЬ", StringComparison.OrdinalIgnoreCase))
        {
            return "Старокостянтинівська МТГ";
        }

        if (lines[0].Contains("Останнє оновлення журналу:", StringComparison.OrdinalIgnoreCase))
        {
            return "system_status";
        }

        foreach (var line in lines.Take(3))
        {
            string cleanTitle = line.Trim('*', '_', ' ', '•', '>');
            if (cleanTitle.StartsWith("ПРОГНОЗ НА ЗАВТРА", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (cleanTitle.Contains("СТАРОКОСТЯНТИНІВ", StringComparison.OrdinalIgnoreCase) &&
                !cleanTitle.Contains("МТГ", StringComparison.OrdinalIgnoreCase) &&
                !cleanTitle.Contains("ГРОМАДА", StringComparison.OrdinalIgnoreCase))
            {
                return isTomorrow ? "tomorrow_starokostiantyniv" : "starokostiantyniv";
            }

            foreach (var canon in TerritoryRegistry.Territories)
            {
                if (cleanTitle.Equals(canon.CanonicalName, StringComparison.OrdinalIgnoreCase) ||
                    cleanTitle.Contains(canon.CenterSettlement, StringComparison.OrdinalIgnoreCase))
                {
                    return isTomorrow ? $"tomorrow_{canon.TerritoryId}" : canon.TerritoryId;
                }
            }
        }

        return null;
    }

    public static string RenderGraphicScheduleCaption(string targetDateStr, string territorialScope = "Старокостянтинівська МТГ")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"*ГРАФІК ПОГОДИННИХ ВІДКЛЮЧЕНЬ | {territorialScope.ToUpperInvariant()}*");
        string fullDate = FormatFullUkrainianDate(targetDateStr);
        if (!string.IsNullOrWhiteSpace(fullDate))
        {
            sb.AppendLine($"*{fullDate}*");
        }
        sb.AppendLine();
        sb.AppendLine("_Графік застосовується за розпорядженням НЕК «Укренерго» для 12 підчерг громади_");
        return CleanOutput(sb.ToString());
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
        var localTime = KyivClock.ToKyiv(utcTime);
        var sb = new StringBuilder();
        sb.AppendLine($"Останнє оновлення журналу: *{localTime:HH:mm}*");
        sb.AppendLine($"Стан моніторингу: {monitoringState}");
        return CleanOutput(sb.ToString());
    }

    public static string RenderArchivedSystemStatus(DateTime utcTime, string previousDateStr)
    {
        var localTime = KyivClock.ToKyiv(utcTime);
        var sb = new StringBuilder();
        sb.AppendLine($"Останнє оновлення журналу: *{localTime:HH:mm}*");

        if (DateTime.TryParse(previousDateStr, out var dt) ||
            DateTime.TryParseExact(previousDateStr, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out dt))
        {
            string day = dt.Day.ToString();
            string month = dt.Month >= 1 && dt.Month <= 12 ? UkrainianMonthGenitive[dt.Month] : dt.Month.ToString();
            string year = dt.Year.ToString();
            sb.AppendLine($"{day} {month} {year} року");
        }
        else
        {
            sb.AppendLine(previousDateStr);
        }

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
                sb.AppendLine("> *АВАРІЙНІ ЗНЕСТРУМЛЕННЯ*");
                if (!string.IsNullOrEmpty(block.TimeInterval))
                {
                    sb.AppendLine($"> *{block.TimeInterval}*");
                }

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

                    if (i > 0)
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine("*ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*");
                    if (!string.IsNullOrEmpty(block.TimeInterval))
                    {
                        sb.AppendLine($"*{block.TimeInterval}*");
                    }
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
        result = Regex.Replace(result, @"<b>(?:(?:м\.|Місто)\s+)?Старокостянтинів</b>", "*м. СТАРОКОСТЯНТИНІВ*", RegexOptions.IgnoreCase);
        result = Regex.Replace(result, @"^\*?(?:(?:м\.|Місто)\s+)?Старокостянтинів\*?$", "*м. СТАРОКОСТЯНТИНІВ*", RegexOptions.Multiline | RegexOptions.IgnoreCase);

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
                    // Convert header with time to clean markdown before replacing general bold
                    cleanLine = Regex.Replace(cleanLine, @"<b>([А-ЯІЇЄҐA-Z\s]+?)\s*\(\s*(\d{2}:\d{2})\s*(?:–|-|до|по)\s*(\d{2}:\d{2})\s*\)</b>", "*$1*\n*$2 – $3*");
                    cleanLine = Regex.Replace(cleanLine, @"<b>(.*?)</b>", "*$1*");
                    foreach (var subLine in cleanLine.Split('\n'))
                    {
                        string subTrim = subLine.Trim();
                        if (!string.IsNullOrEmpty(subTrim))
                        {
                            sbQuote.AppendLine($"> {subTrim}");
                        }
                    }
                }
            }
            return sbQuote.ToString().TrimEnd();
        });

        // Convert headers with time in parentheses directly to avoid nested asterisks:
        // <b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (10:00 – 17:00)</b> -> *ПЛАНОВІ ЗНЕСТРУМЛЕННЯ*\n*10:00 – 17:00*
        result = Regex.Replace(result, @"<b>([А-ЯІЇЄҐA-Z\s]+?)\s*\(\s*(\d{2}:\d{2})\s*(?:–|-|до|по)\s*(\d{2}:\d{2})\s*\)</b>", "*$1*\n*$2 – $3*");

        // Convert HTML tags to WhatsApp Markdown
        result = Regex.Replace(result, @"<b>(.*?)</b>", "*$1*");
        result = Regex.Replace(result, @"<i>(.*?)</i>", "_$1_");
        result = Regex.Replace(result, @"<code>(.*?)</code>", "*$1*");

        // Convert (HH:mm–HH:mm) into unified bold badges • *HH:mm – HH:mm*
        result = Regex.Replace(result, @"\(\s*(\d{2}:\d{2})\s*(?:–|-|до|по)\s*(\d{2}:\d{2})\s*\)", "• *$1 – $2*");

        // Convert any remaining inline section headers (e.g. *ПЛАНОВІ ЗНЕСТРУМЛЕННЯ* • *10:00 – 17:00*) into two lines
        result = Regex.Replace(result, @"(?m)^>\s*\*?((?:АВАРІЙНІ|ПЛАНОВІ)\s+ЗНЕСТРУМЛЕННЯ)\*?\s*•\s*\*?(\d{2}:\d{2}\s*(?:–|-|до|по)\s*\d{2}:\d{2})\*?", "> *$1*\n> *$2*");
        result = Regex.Replace(result, @"(?m)^\*?((?:АВАРІЙНІ|ПЛАНОВІ)\s+ЗНЕСТРУМЛЕННЯ)\*?\s*•\s*\*?(\d{2}:\d{2}\s*(?:–|-|до|по)\s*\d{2}:\d{2})\*?", "*$1*\n*$2*");

        // Clean up any accidental nested bold asterisks: *HEADER • *TIME** -> *HEADER* • *TIME*
        result = Regex.Replace(result, @"\*([^*\n]+?)\s*•\s*\*([^*\n]+?)\*\*", "*$1* • *$2*");

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
