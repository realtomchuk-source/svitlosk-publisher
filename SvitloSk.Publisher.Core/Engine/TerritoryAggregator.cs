using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SvitloSk.Publisher.Core.Engine;

public record AggregatedTerritoryData(
    string TerritoryId,
    string CanonicalName,
    IReadOnlyList<OutageRecord> EmergencyRecords,
    IReadOnlyList<OutageRecord> PlannedRecords
);

public record JournalSummaryStats(
    int TotalTerritories,
    IReadOnlyList<string> PlannedSettlements,
    IReadOnlyList<string> EmergencySettlements
);

public class TerritoryAggregator
{
    public static List<AggregatedTerritoryData> AggregateByTerritory(IReadOnlyList<OutageRecord> records)
    {
        var result = new List<AggregatedTerritoryData>();
        if (records == null || records.Count == 0)
        {
            return result;
        }

        var groups = new Dictionary<string, (string CanonicalName, List<OutageRecord> Emergency, List<OutageRecord> Planned)>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.TerritoryName) || record.TerritoryName.Equals("Громада", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string territoryId;
            string canonicalName;
            try
            {
                territoryId = TerritoryRegistry.MapRawTerritoryName(record.TerritoryName);
                canonicalName = TerritoryRegistry.Territories.FirstOrDefault(t => t.TerritoryId.Equals(territoryId, StringComparison.OrdinalIgnoreCase))?.CanonicalName ?? record.TerritoryName;
            }
            catch
            {
                territoryId = record.TerritoryName;
                canonicalName = record.TerritoryName;
            }

            if (!groups.TryGetValue(territoryId, out var groupData))
            {
                groupData = (canonicalName, new List<OutageRecord>(), new List<OutageRecord>());
                groups[territoryId] = groupData;
            }

            if (record.OutageType.Equals("АВАРІЙНІ", StringComparison.OrdinalIgnoreCase))
            {
                groupData.Emergency.Add(record);
            }
            else
            {
                groupData.Planned.Add(record);
            }
        }

        // Deterministic ordering: starokostiantyniv first, then alphabetical by canonical name
        var orderedKeys = groups.Keys.OrderBy(k => k.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                     .ThenBy(k => groups[k].CanonicalName, StringComparer.CurrentCultureIgnoreCase);

        foreach (var key in orderedKeys)
        {
            var data = groups[key];
            result.Add(new AggregatedTerritoryData(key, data.CanonicalName, data.Emergency, data.Planned));
        }

        return result;
    }

    public static JournalSummaryStats CalculateSummaryStats(IReadOnlyList<OutageRecord> records)
    {
        var aggregated = AggregateByTerritory(records);
        var plannedRecords = aggregated.SelectMany(a => a.PlannedRecords).ToList();
        var emergencyRecords = aggregated.SelectMany(a => a.EmergencyRecords).ToList();

        var plannedSettlements = ExtractSettlements(plannedRecords);
        var emergencySettlements = ExtractSettlements(emergencyRecords);

        return new JournalSummaryStats(
            TotalTerritories: aggregated.Count,
            PlannedSettlements: plannedSettlements,
            EmergencySettlements: emergencySettlements
        );
    }

    public static List<string> ExtractSettlements(IReadOnlyList<OutageRecord> records)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Details)) continue;

            // 1. Scan for explicit settlement mentions in Details e.g. "с. Мацевичі", "с. Великі Мацевичі", "м. Старокостянтинів"
            var matches = Regex.Matches(record.Details, @"\b(с\.\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\-]+(?:\s+[А-Яа-яA-Za-zіІїЇєЄґҐ'\-]+)?|м\.\s*Старокостянтинів|селище\s*[А-Яа-яA-Za-zіІїЇєЄґҐ'\-]+)\b");
            bool foundExplicit = false;
            foreach (Match m in matches)
            {
                string raw = m.Value.Trim();
                // Clean up trailing punctuation if any
                raw = Regex.Replace(raw, @"\s+", " ").Trim();
                if (!string.IsNullOrWhiteSpace(raw) && !raw.Equals("с.", StringComparison.OrdinalIgnoreCase))
                {
                    set.Add(raw);
                    foundExplicit = true;
                }
            }

            // 2. If no explicit "с. ..." found in details, infer from territory definition / CenterSettlement
            if (!foundExplicit && !string.IsNullOrWhiteSpace(record.TerritoryName))
            {
                try
                {
                    string territoryId = TerritoryRegistry.MapRawTerritoryName(record.TerritoryName);
                    var canonical = TerritoryRegistry.Territories.FirstOrDefault(t => t.TerritoryId.Equals(territoryId, StringComparison.OrdinalIgnoreCase));
                    if (canonical != null)
                    {
                        if (canonical.Type == TerritoryType.CITY)
                        {
                            set.Add("м. Старокостянтинів");
                        }
                        else if (!string.IsNullOrWhiteSpace(canonical.CenterSettlement))
                        {
                            set.Add($"с. {canonical.CenterSettlement}");
                        }
                    }
                }
                catch
                {
                    // Fallback to record territory name if mapping fails
                    set.Add(record.TerritoryName);
                }
            }
        }

        // Sort: "м. Старокостянтинів" first, then alphabetically
        return set.OrderBy(s => s.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                  .ThenBy(s => s, StringComparer.CurrentCultureIgnoreCase)
                  .ToList();
    }

    public static string CompactHouseNumbers(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // Match sequence of comma-separated pure numbers e.g. "1, 2, 3, 4, 5, 6, 7" or "буд. 1, 2, 3, 4, 5"
        // Regex to find comma-separated numbers and compact them into ranges
        return Regex.Replace(text, @"\b(\d+)(,\s*\d+){2,}\b", match =>
        {
            var parts = match.Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new List<int>();
            bool allNumbers = true;
            foreach (var p in parts)
            {
                if (int.TryParse(p, out int val))
                {
                    numbers.Add(val);
                }
                else
                {
                    allNumbers = false;
                    break;
                }
            }

            if (!allNumbers || numbers.Count < 3)
            {
                return match.Value;
            }

            // Check if sequential or can be grouped into ranges
            var ranges = new List<string>();
            int start = numbers[0];
            int prev = start;

            for (int i = 1; i < numbers.Count; i++)
            {
                int curr = numbers[i];
                if (curr == prev + 1)
                {
                    prev = curr;
                }
                else
                {
                    if (start == prev)
                        ranges.Add(start.ToString());
                    else if (prev == start + 1)
                        ranges.Add($"{start}, {prev}");
                    else
                        ranges.Add($"{start}–{prev}");

                    start = curr;
                    prev = curr;
                }
            }

            if (start == prev)
                ranges.Add(start.ToString());
            else if (prev == start + 1)
                ranges.Add($"{start}, {prev}");
            else
                ranges.Add($"{start}–{prev}");

            return string.Join(", ", ranges);
        });
    }
}
