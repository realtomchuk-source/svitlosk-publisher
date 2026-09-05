using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace SvitloSk.Publisher.Core.Engine;

public class OutageFeedParser : IOutageFeedParser
{
    public IReadOnlyList<OutageRecord> Parse(string rawFeed)
    {
        var records = new List<OutageRecord>();
        if (string.IsNullOrWhiteSpace(rawFeed))
        {
            return records;
        }

        // Split raw feed into lines
        string normalized = rawFeed.Replace("\r\n", "\n").Replace("\r", "\n");
        string[] lines = normalized.Split('\n');

        string currentOutageType = "UNKNOWN";
        StringBuilder currentBlock = new StringBuilder();
        string currentTerritoryName = "";

        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            // Detect section headers
            if (line.Contains("--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---"))
            {
                FlushCurrentBlock(records, currentTerritoryName, currentOutageType, currentBlock);
                currentOutageType = "АВАРІЙНІ";
                currentTerritoryName = "";
                continue;
            }
            if (line.Contains("--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---"))
            {
                FlushCurrentBlock(records, currentTerritoryName, currentOutageType, currentBlock);
                currentOutageType = "ПЛАНОВІ";
                currentTerritoryName = "";
                continue;
            }

            // Detect territory brackets like [Місто Старокостянтинів] or numbered settlement format
            var territoryMatch = Regex.Match(line, @"^\[(.*?)\]$");
            var settlementMatch = Regex.Match(line, @"^\d+\.\s*(?:Населений пункт:\s*)?(.*)$", RegexOptions.IgnoreCase);

            if (territoryMatch.Success)
            {
                FlushCurrentBlock(records, currentTerritoryName, currentOutageType, currentBlock);
                currentTerritoryName = territoryMatch.Groups[1].Value.Trim();
                continue;
            }
            else if (settlementMatch.Success && !line.Contains("---"))
            {
                FlushCurrentBlock(records, currentTerritoryName, currentOutageType, currentBlock);
                currentTerritoryName = settlementMatch.Groups[1].Value.Trim();
                continue;
            }


            // If we are parsing a block and have a territory name, collect details
            if (!string.IsNullOrEmpty(currentTerritoryName))
            {
                if (line.StartsWith("===") || line.StartsWith("КІНЕЦЬ") || line.StartsWith("Кількість") || line.StartsWith("ДАНІ") || line.StartsWith("Дата:") || line.StartsWith("Джерело:") || line.StartsWith("Останнє") || line.StartsWith("Перша") || line.StartsWith("Статус:") || line.StartsWith("Хеш") || line.StartsWith("Історія"))
                {
                    continue;
                }
                currentBlock.AppendLine(line);
            }
            else if (currentOutageType == "АВАРІЙНІ" && line.Contains("Аварійних знеструмлень не зафіксовано"))
            {
                // General empty state indicator
                records.Add(new OutageRecord("Громада", "АВАРІЙНІ", line, null));
            }
            else if (currentOutageType == "ПЛАНОВІ" && line.Contains("Планових знеструмлень не зафіксовано"))
            {
                records.Add(new OutageRecord("Громада", "ПЛАНОВІ", line, null));
            }
        }

        FlushCurrentBlock(records, currentTerritoryName, currentOutageType, currentBlock);

        return records;
    }

    private void FlushCurrentBlock(List<OutageRecord> records, string territory, string type, StringBuilder block)
    {
        if (string.IsNullOrEmpty(territory) || block.Length == 0)
        {
            block.Clear();
            return;
        }

        string details = block.ToString().Trim();
        block.Clear();

        // Extract queues/subqueues using a regex if present in the text (e.g. Черга, Черги, 1 черга, 1.1 тощо)
        // Check for common Ukrainian queue formats: "Черга: 1", "Черга 1", "черги: 2", "1.1", "1 черга"
        string? queues = null;
        var queueMatches = Regex.Matches(details, @"(\d+(\.\d+)?)\s*(черг[аи]|черга)", RegexOptions.IgnoreCase);
        if (queueMatches.Count > 0)
        {
            var qList = new List<string>();
            foreach (Match match in queueMatches)
            {
                qList.Add(match.Groups[1].Value);
            }
            queues = string.Join(", ", qList);
        }
        else
        {
            // Fallback check for "Черг[аи]: 1, 2" or "Черга 1"
            var alternativeMatch = Regex.Match(details, @"черг[аи]\s*:?\s*([\d\s\,\.\/]+)", RegexOptions.IgnoreCase);
            if (alternativeMatch.Success)
            {
                queues = alternativeMatch.Groups[1].Value.Trim();
            }
        }

        records.Add(new OutageRecord(territory, type, details, queues));
    }

    public GraphicInputPackage ParseGraphicSchedule(string rawFeed, string editionDate, string territorialScope = "Старокостянтинівська МТГ")
    {
        // 1. Initialize canonical structure of 6 queues and 12 subqueues
        var subqueueMap = new Dictionary<string, List<GraphicInterval>>(StringComparer.OrdinalIgnoreCase);
        for (int q = 1; q <= 6; q++)
        {
            subqueueMap[$"Черга {q}.1"] = new List<GraphicInterval>();
            subqueueMap[$"Черга {q}.2"] = new List<GraphicInterval>();
        }

        if (!string.IsNullOrWhiteSpace(rawFeed))
        {
            var records = Parse(rawFeed);

            foreach (var record in records)
            {
                if (string.IsNullOrWhiteSpace(record.Details))
                    continue;

                // Determine default interval status from outage type
                string status = record.OutageType.Equals("ПЛАНОВІ", StringComparison.OrdinalIgnoreCase) 
                    ? "Restricted" 
                    : "Possible";

                // Look for subqueues mentioned in the record
                var targetSubqueues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(record.Queues))
                {
                    var tokens = record.Queues.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var token in tokens)
                    {
                        var trimmed = token.Trim();
                        // Format "1.1" -> "Черга 1.1"
                        if (Regex.IsMatch(trimmed, @"^[1-6]\.[1-2]$"))
                        {
                            targetSubqueues.Add($"Черга {trimmed}");
                        }
                        // Format "1" -> "Черга 1.1" and "Черга 1.2"
                        else if (int.TryParse(trimmed, out int qNum) && qNum >= 1 && qNum <= 6)
                        {
                            targetSubqueues.Add($"Черга {qNum}.1");
                            targetSubqueues.Add($"Черга {qNum}.2");
                        }
                    }
                }

                // If no subqueue token parsed from Queues field, scan details text for "Черга X.Y" or "X.Y"
                if (targetSubqueues.Count == 0)
                {
                    var directMatches = Regex.Matches(record.Details, @"(?:черг[аи]\s*)?([1-6]\.[1-2])", RegexOptions.IgnoreCase);
                    foreach (Match m in directMatches)
                    {
                        targetSubqueues.Add($"Черга {m.Groups[1].Value}");
                    }
                }

                // Extract time intervals: "HH:MM - HH:MM" or "HH:MM–HH:MM"
                var timeMatches = Regex.Matches(record.Details, @"(\d{1,2}:\d{2})\s*[-–—]\s*(\d{1,2}:\d{2})");
                var extractedIntervals = new List<GraphicInterval>();
                foreach (Match tm in timeMatches)
                {
                    string startRaw = tm.Groups[1].Value;
                    string endRaw = tm.Groups[2].Value;

                    if (TimeSpan.TryParse(startRaw, out var sTs) && (endRaw == "24:00" || TimeSpan.TryParse(endRaw, out _)))
                    {
                        string formattedStart = $"{sTs.Hours:D2}:{sTs.Minutes:D2}";
                        string formattedEnd = endRaw == "24:00" ? "24:00" : $"{TimeSpan.Parse(endRaw).Hours:D2}:{TimeSpan.Parse(endRaw).Minutes:D2}";
                        
                        if (sTs < (endRaw == "24:00" ? TimeSpan.FromHours(24) : TimeSpan.Parse(endRaw)))
                        {
                            extractedIntervals.Add(new GraphicInterval(formattedStart, formattedEnd, status));
                        }
                    }
                }

                // Assign intervals to target subqueues
                if (targetSubqueues.Count > 0 && extractedIntervals.Count > 0)
                {
                    foreach (var sqKey in targetSubqueues)
                    {
                        if (subqueueMap.TryGetValue(sqKey, out var intList))
                        {
                            foreach (var interval in extractedIntervals)
                            {
                                if (!intList.Any(existing => existing.StartTime == interval.StartTime && existing.EndTime == interval.EndTime && existing.Status == interval.Status))
                                {
                                    intList.Add(interval);
                                }
                            }
                        }
                    }
                }
            }
        }

        // Build canonical 6 queues and 12 subqueues
        var queues = new List<QueueSchedule>();
        for (int q = 1; q <= 6; q++)
        {
            var subqueues = new List<SubqueueSchedule>
            {
                new SubqueueSchedule($"Черга {q}.1", subqueueMap[$"Черга {q}.1"]),
                new SubqueueSchedule($"Черга {q}.2", subqueueMap[$"Черга {q}.2"])
            };
            queues.Add(new QueueSchedule($"Черга {q}", subqueues));
        }

        var metadata = new GraphicMetadata(
            PackageId: Guid.NewGuid().ToString("D"),
            GenerationTimestamp: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            TargetDate: editionDate,
            SourceIdentifier: "DSO-KHM-OUTAGES"
        );

        return new GraphicInputPackage(
            Metadata: metadata,
            TerritorialScope: territorialScope,
            Queues: queues
        );
    }

    public GraphicInputPackage ParseLegacyGraphicJson(string legacyJson, string territorialScope = "Старокостянтинівська МТГ")
    {
        if (string.IsNullOrWhiteSpace(legacyJson))
            throw new ArgumentException("Legacy JSON content cannot be null or empty.", nameof(legacyJson));

        using var doc = System.Text.Json.JsonDocument.Parse(legacyJson);
        var root = doc.RootElement;

        string targetDate = root.TryGetProperty("date", out var dateElem) 
            ? dateElem.GetString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd") 
            : DateTime.UtcNow.ToString("yyyy-MM-dd");

        string genTimestamp = root.TryGetProperty("updated_at", out var upElem)
            ? upElem.GetString() ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            : DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        // Parse queues object
        var subqueueMap = new Dictionary<string, List<GraphicInterval>>(StringComparer.OrdinalIgnoreCase);
        for (int q = 1; q <= 6; q++)
        {
            subqueueMap[$"Черга {q}.1"] = new List<GraphicInterval>();
            subqueueMap[$"Черга {q}.2"] = new List<GraphicInterval>();
        }

        if (root.TryGetProperty("queues", out var queuesObj) && queuesObj.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            foreach (var prop in queuesObj.EnumerateObject())
            {
                string sqKey = prop.Name.Trim(); // e.g. "1.1", "2.2"
                string canonicalSqId = sqKey.StartsWith("Черга", StringComparison.OrdinalIgnoreCase) 
                    ? sqKey 
                    : $"Черга {sqKey}";

                if (!subqueueMap.ContainsKey(canonicalSqId))
                {
                    continue;
                }

                string mask = prop.Value.GetString() ?? string.Empty;
                if (mask.Length == 24)
                {
                    // Convert 24-character bitmask (0 = outage / Restricted, 1 = powered, 2 = possible / Possible)
                    int hour = 0;
                    while (hour < 24)
                    {
                        char state = mask[hour];
                        if (state == '0' || state == '2')
                        {
                            int startHour = hour;
                            char currentStatusChar = state;
                            while (hour < 24 && mask[hour] == currentStatusChar)
                            {
                                hour++;
                            }
                            int endHour = hour;

                            string startStr = $"{startHour:D2}:00";
                            string endStr = endHour == 24 ? "24:00" : $"{endHour:D2}:00";
                            string status = currentStatusChar == '2' ? "Possible" : "Restricted";

                            subqueueMap[canonicalSqId].Add(new GraphicInterval(startStr, endStr, status));
                        }
                        else
                        {
                            hour++;
                        }
                    }
                }
            }
        }

        var queues = new List<QueueSchedule>();
        for (int q = 1; q <= 6; q++)
        {
            var subqueues = new List<SubqueueSchedule>
            {
                new SubqueueSchedule($"Черга {q}.1", subqueueMap[$"Черга {q}.1"]),
                new SubqueueSchedule($"Черга {q}.2", subqueueMap[$"Черга {q}.2"])
            };
            queues.Add(new QueueSchedule($"Черга {q}", subqueues));
        }

        var metadata = new GraphicMetadata(
            PackageId: Guid.NewGuid().ToString("D"),
            GenerationTimestamp: genTimestamp,
            TargetDate: targetDate,
            SourceIdentifier: "LEGACY-TG-POSTS"
        );

        return new GraphicInputPackage(
            Metadata: metadata,
            TerritorialScope: territorialScope,
            Queues: queues
        );
    }
}


