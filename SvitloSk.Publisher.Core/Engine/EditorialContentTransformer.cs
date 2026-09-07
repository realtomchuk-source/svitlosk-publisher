using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace SvitloSk.Publisher.Core.Engine;

public class EditorialContentTransformer
{
    public string MapTerritory(string rawTerritoryName)
    {
        return TerritoryRegistry.MapRawTerritoryName(rawTerritoryName);
    }

    public static string FormatDate(string dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return dateStr;
        var match = Regex.Match(dateStr, @"(\d{4})-(\d{2})-(\d{2})");
        if (match.Success)
        {
            return $"{match.Groups[3].Value}.{match.Groups[2].Value}.{match.Groups[1].Value}";
        }
        return dateStr;
    }

    private static string NormalizeShortName(string canonicalName)
    {
        return canonicalName.Replace(" старостинський округ", "")
                            .Replace("Місто ", "м. ");
    }

    private static string GetDistrictPlural(int count)
    {
        int mod10 = count % 10;
        int mod100 = count % 100;
        if (mod100 >= 11 && mod100 <= 19) return "округів";
        if (mod10 == 1) return "округ";
        if (mod10 >= 2 && mod10 <= 4) return "округи";
        return "округів";
    }

    public static string GetUkrainianDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => "Понеділок",
            DayOfWeek.Tuesday => "Вівторок",
            DayOfWeek.Wednesday => "Середа",
            DayOfWeek.Thursday => "Четвер",
            DayOfWeek.Friday => "П'ятниця",
            DayOfWeek.Saturday => "Субота",
            DayOfWeek.Sunday => "Неділя",
            _ => "Сьогодні"
        };
    }

    public string RenderJournalHeader(string editionDate, JournalSummaryStats stats)
    {
        string formattedDate = FormatDate(editionDate);
        
        // Extract day of week from date if possible
        string dayOfWeekStr = "Сьогодні";
        if (DateTime.TryParse(editionDate, out var dt))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(dt.DayOfWeek);
        }
        else if (DateTime.TryParseExact(formattedDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dtExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(dtExact.DayOfWeek);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"<blockquote><b>{dayOfWeekStr} {formattedDate}</b></blockquote>");
        sb.AppendLine("Старокостянтинівська територіальна громада");
        sb.AppendLine();

        if (stats.PlannedSettlements.Count > 0)
        {
            string pList = string.Join(", ", stats.PlannedSettlements);
            sb.AppendLine("<b>Планові знеструмлення:</b>");
            sb.AppendLine(pList);
        }
        else
        {
            sb.AppendLine("<b>Планові знеструмлення:</b> відсутні");
        }

        sb.AppendLine();

        if (stats.EmergencySettlements.Count > 0)
        {
            string eList = string.Join(", ", stats.EmergencySettlements);
            sb.AppendLine("<b>Аварійні знеструмлення:</b>");
            sb.AppendLine(eList);
        }
        else
        {
            sb.AppendLine("<b>Аварійні знеструмлення:</b> відсутні");
        }

        return sb.ToString().TrimEnd();
    }

    public string RenderSystemStatus()
    {
        return $"Останнє оновлення журналу: {DateTime.UtcNow.AddHours(3):HH:mm}\nСтан моніторингу: активний";
    }

    public string RenderAggregatedTerritoryPost(AggregatedTerritoryData data, bool isTomorrow = false, string? tomorrowDate = null)
    {
        var sb = new StringBuilder();

        if (isTomorrow && !string.IsNullOrWhiteSpace(tomorrowDate))
        {
            sb.AppendLine($"<b>ПРОГНОЗ НА ЗАВТРА — {FormatDate(tomorrowDate)}</b>");
        }

        string territoryTitle = HttpUtility.HtmlEncode(data.CanonicalName);
        sb.AppendLine($"<b>{territoryTitle}</b>");
        sb.AppendLine();

        // 1. Emergency Block (Rendered inside <blockquote>)
        if (data.EmergencyRecords != null && data.EmergencyRecords.Count > 0)
        {
            string emergTime = ExtractCommonTimeInterval(data.EmergencyRecords);
            if (!string.IsNullOrEmpty(emergTime))
            {
                sb.AppendLine($"<blockquote><b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ({emergTime})</b>");
                foreach (var rec in data.EmergencyRecords)
                {
                    string body = RenderRecordDetails(rec.Details, emergTime, data.CanonicalName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                }
                sb.AppendLine("</blockquote>");
                sb.AppendLine();
            }
            else
            {
                // Group by interval if intervals differ
                foreach (var rec in data.EmergencyRecords)
                {
                    string recTime = ExtractCommonTimeInterval(new[] { rec });
                    string recHeader = !string.IsNullOrEmpty(recTime)
                        ? $"<b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ({recTime})</b>"
                        : "<b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ</b>";

                    sb.AppendLine($"<blockquote>{recHeader}");
                    string body = RenderRecordDetails(rec.Details, recTime, data.CanonicalName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                    sb.AppendLine("</blockquote>");
                    sb.AppendLine();
                }
            }
        }

        // 2. Planned Block
        if (data.PlannedRecords != null && data.PlannedRecords.Count > 0)
        {
            string planTime = ExtractCommonTimeInterval(data.PlannedRecords);
            if (!string.IsNullOrEmpty(planTime))
            {
                sb.AppendLine($"<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ({planTime})</b>");
                sb.AppendLine();
                foreach (var rec in data.PlannedRecords)
                {
                    string body = RenderRecordDetails(rec.Details, planTime, data.CanonicalName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                }
            }
            else
            {
                // Multiple distinct time groups: render repeated block headers per time group separated by empty line
                for (int i = 0; i < data.PlannedRecords.Count; i++)
                {
                    var rec = data.PlannedRecords[i];
                    string recTime = ExtractCommonTimeInterval(new[] { rec });
                    string recHeader = !string.IsNullOrEmpty(recTime)
                        ? $"<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ({recTime})</b>"
                        : "<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ</b>";

                    if (i > 0)
                    {
                        sb.AppendLine();
                    }
                    sb.AppendLine(recHeader);
                    sb.AppendLine();

                    string body = RenderRecordDetails(rec.Details, recTime, data.CanonicalName);
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        sb.AppendLine(body);
                    }
                }
            }
        }

        string output = sb.ToString().TrimEnd();
        return output.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private string ExtractCommonTimeInterval(IReadOnlyList<OutageRecord> records)
    {
        var intervals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in records)
        {
            if (string.IsNullOrWhiteSpace(rec.Details)) continue;
            var lines = rec.Details.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var settlementMatch = Regex.Match(line, @"^(?:с\.|м\.|селище).*?\|\s*(.*)$", RegexOptions.IgnoreCase);
                if (settlementMatch.Success)
                {
                    string shortRange = FormatTimeIntervalToShortRange(settlementMatch.Groups[1].Value.Trim());
                    if (!string.IsNullOrEmpty(shortRange))
                    {
                        intervals.Add(shortRange);
                    }
                }
                else
                {
                    var timeMatch = Regex.Match(line.Trim(), @"^(?:з\s*)?(\d{2}:\d{2})\s*(?:по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
                    if (timeMatch.Success)
                    {
                        intervals.Add($"{timeMatch.Groups[1].Value}–{timeMatch.Groups[2].Value}");
                    }
                }
            }
        }

        return intervals.Count == 1 ? intervals.First() : string.Empty;
    }

    public string RenderTemplate(OutageRecord record)
    {
        return RenderTemplate(record, "СЬОГОДНІ");
    }

    public string RenderTemplate(OutageRecord record, string dateLabel)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));

        string territoryId;
        string canonicalName;
        try
        {
            territoryId = MapTerritory(record.TerritoryName);
            canonicalName = TerritoryRegistry.Territories.FirstOrDefault(t => t.TerritoryId.Equals(territoryId, StringComparison.OrdinalIgnoreCase))?.CanonicalName ?? record.TerritoryName;
        }
        catch
        {
            territoryId = record.TerritoryName;
            canonicalName = record.TerritoryName;
        }

        var aggData = record.OutageType.Equals("АВАРІЙНІ", StringComparison.OrdinalIgnoreCase)
            ? new AggregatedTerritoryData(territoryId, canonicalName, new[] { record }, Array.Empty<OutageRecord>())
            : new AggregatedTerritoryData(territoryId, canonicalName, Array.Empty<OutageRecord>(), new[] { record });

        return RenderAggregatedTerritoryPost(aggData);
    }

    public string RenderRecordDetails(string details, string? commonTimeInterval = null, string? canonicalTerritoryName = null)
    {
        if (string.IsNullOrWhiteSpace(details)) return string.Empty;

        // Clean queues
        string clean = HttpUtility.HtmlEncode(details);
        clean = Regex.Replace(clean, @"[⚡📋]?\s*черг[аи]\s*:\s*\d+(\.\d+)?", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\d+(\.\d+)?\s*черг[аи]", "", RegexOptions.IgnoreCase);
        clean = clean.Replace("\r\n", "\n").Replace("\r", "\n");

        var sb = new StringBuilder();
        var lines = clean.Split('\n');

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Remove leading bullet characters or double dashes if present
            line = Regex.Replace(line, @"^[•\-\*\s]+", "").Trim();

            // Check if line is a settlement header e.g. "с. Великі Мацевичі | з 10:45 до 16:45" or "м. Старокостянтинів | з 09:00 до 17:00"
            var settlementMatch = Regex.Match(line, @"^((?:с\.|м\.|селище)\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s*\|\s*(.*)$", RegexOptions.IgnoreCase);
            if (settlementMatch.Success)
            {
                string settlement = settlementMatch.Groups[1].Value.Trim();
                string timePart = settlementMatch.Groups[2].Value.Trim();
                string interval = FormatTimeIntervalToShortRange(timePart);

                // If this is the city/territory itself (e.g. "м. Старокостянтинів" inside "Місто Старокостянтинів"),
                // omit the redundant settlement header entirely (Variant 1).
                bool isRedundantCityHeader = !string.IsNullOrEmpty(canonicalTerritoryName) &&
                    (canonicalTerritoryName.Contains(settlement, StringComparison.OrdinalIgnoreCase) ||
                     settlement.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) && canonicalTerritoryName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase));

                if (!isRedundantCityHeader)
                {
                    // If interval is already shown in the block header, omit from village line for clean look
                    if (!string.IsNullOrEmpty(interval) && (string.IsNullOrEmpty(commonTimeInterval) || !interval.Equals(commonTimeInterval, StringComparison.OrdinalIgnoreCase)))
                    {
                        sb.AppendLine($"<b>{settlement}</b> ({interval})");
                    }
                    else
                    {
                        sb.AppendLine($"<b>{settlement}</b>");
                    }
                }
                continue;
            }

            // Check if line is a pure time interval
            var timeMatch = Regex.Match(line, @"^(з\s*)?(\d{2}:\d{2})\s*(по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
            if (timeMatch.Success)
            {
                string interval = $"{timeMatch.Groups[2].Value}–{timeMatch.Groups[4].Value}";
                sb.AppendLine($"Час: {interval}");

                string remainder = line.Substring(timeMatch.Length).Trim(' ', ':', ',', '-').Trim();
                if (!string.IsNullOrEmpty(remainder))
                {
                    string compacted = TerritoryAggregator.CompactHouseNumbers(FormatAddressLine(remainder));
                    sb.AppendLine($"- {compacted}");
                }
            }
            else
            {
                if (line.Contains("не зафіксовано") || line.Contains("не заплановано"))
                {
                    sb.AppendLine(line);
                }
                else
                {
                    string compacted = TerritoryAggregator.CompactHouseNumbers(FormatAddressLine(line));
                    sb.AppendLine($"- {compacted}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private string FormatTimeIntervalToShortRange(string timePart)
    {
        if (string.IsNullOrWhiteSpace(timePart)) return string.Empty;
        var match = Regex.Match(timePart, @"(\d{2}:\d{2})\s*(?:по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"{match.Groups[1].Value}–{match.Groups[2].Value}";
        }
        return timePart;
    }

    private string FormatTimeInterval(string timePart)
    {
        var match = Regex.Match(timePart, @"(\d{2}:\d{2})\s*(?:по|до|-|–)\s*(\d{2}:\d{2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return $"з {match.Groups[1].Value} до {match.Groups[2].Value}";
        }
        return timePart;
    }

    private string FormatAddressLine(string line)
    {
        string formatted = Regex.Replace(line, @":(?!\d{2})", ",");
        formatted = Regex.Replace(formatted, @"(вул\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1, $2");
        formatted = Regex.Replace(formatted, @"(пров\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(\d+)", "$1, $2");
        formatted = Regex.Replace(formatted, @"(с\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\s-]+?)\s+(вул\.)", "$1, $2");
        formatted = Regex.Replace(formatted, @",\s*,", ",");
        formatted = Regex.Replace(formatted, @"\s+", " ");
        return formatted.Trim(' ', ',').Trim();
    }

    public List<TransformedPackage> TransformFeed(IReadOnlyList<OutageRecord> parsedRecords)
    {
        return TransformFeed(parsedRecords, "СЬОГОДНІ");
    }

    public List<TransformedPackage> TransformFeed(IReadOnlyList<OutageRecord> parsedRecords, string dateLabel)
    {
        var packages = new List<TransformedPackage>();
        if (parsedRecords == null || parsedRecords.Count == 0)
        {
            return packages;
        }

        // 1. Calculate Summary Stats and create journal_header as first package
        var stats = TerritoryAggregator.CalculateSummaryStats(parsedRecords);
        if (stats.TotalTerritories > 0)
        {
            string headerContent = RenderJournalHeader(dateLabel, stats);
            packages.Add(new TransformedPackage(
                "journal_header",
                headerContent,
                null,
                true
            ));
        }

        // 2. Aggregate records per territory (1 post per territory)
        var aggregatedTerritories = TerritoryAggregator.AggregateByTerritory(parsedRecords);
        foreach (var agg in aggregatedTerritories)
        {
            string formattedContent = RenderAggregatedTerritoryPost(agg);

            // Safe split if exceeding Telegram limit
            var splitMsgs = SplitTelegramMessage(formattedContent, 3800);
            foreach (var chunk in splitMsgs)
            {
                packages.Add(new TransformedPackage(
                    agg.TerritoryId,
                    chunk,
                    null,
                    true
                ));
            }
        }

        return packages;
    }

    public List<string> SplitTelegramMessage(string text, int limit)
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

        for (int i = 0; i < result.Count; i++)
        {
            result[i] = result[i].Replace("\r\n", "\n").Replace("\r", "\n");
        }

        return result;
    }
}
