using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Domain;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SvitloSk.Publisher.Runtime;

public class RealOutagesSkInputPackageProvider : IInputPackageProvider
{
    private readonly HttpClient _httpClient;
    private readonly InputSourcesOptions _options;
    private readonly ILogger<RealOutagesSkInputPackageProvider> _logger;
    private static readonly TimeZoneInfo KyivTz;

    static RealOutagesSkInputPackageProvider()
    {
        try
        {
            KyivTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Kyiv");
        }
        catch (TimeZoneNotFoundException)
        {
            KyivTz = TimeZoneInfo.FindSystemTimeZoneById("FLE Standard Time");
        }
    }

    public RealOutagesSkInputPackageProvider(
        HttpClient httpClient,
        IOptions<InputSourcesOptions> options,
        ILogger<RealOutagesSkInputPackageProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private class JsonSourceMetadata
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }
        
        [JsonPropertyName("mode")]
        public string? Mode { get; set; }
        
        [JsonPropertyName("queues")]
        public Dictionary<string, string>? Queues { get; set; }
    }

    public async Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
    {
        var events = new List<Event>();
        var territoryPayloads = new Dictionary<string, string>();
        JsonSourceMetadata? metadata = null;
        
        try
        {
            // 1. Determine Target Date (Kyiv Time)
            var currentKyivTime = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, KyivTz);
            var targetDate = currentKyivTime.Date;
            
            // 2. Fetch JSON Metadata
            var jsonUrl = $"{_options.Json.BaseUrl}/{targetDate:yyyy-MM-dd}.json";
            _logger.LogInformation("Fetching JSON from {Url}", jsonUrl);
            var jsonContent = await _httpClient.GetStringAsync(jsonUrl, cancellationToken);
            metadata = JsonSerializer.Deserialize<JsonSourceMetadata>(jsonContent);
            
            if (metadata?.Date != targetDate.ToString("yyyy-MM-dd"))
            {
                throw new InvalidOperationException($"Date mismatch in JSON. Expected: {targetDate:yyyy-MM-dd}, Actual: {metadata?.Date}");
            }
            
            // 3. Fetch Text Markdown
            var textUrl = $"{_options.Text.BaseUrl}/today.txt";
            _logger.LogInformation("Fetching Text from {Url}", textUrl);
            var textContent = await _httpClient.GetStringAsync(textUrl, cancellationToken);
            
            // 4. Parse Text into Events
            events = ParseTextToEvents(textContent, targetDate, territoryPayloads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch or compose dual-source inputs.");
            throw;
        }

        return new InputPackage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "DualSourceProvider",
            "Starokostiantyniv Urban Territorial Community",
            events,
            InputPackageType.Text,
            null,
            DateOnly.FromDateTime(DateTime.UtcNow),
            territoryPayloads,
            metadata?.Queues
        );
    }
    
    private List<Event> ParseTextToEvents(string text, DateTime targetDate, Dictionary<string, string> territoryPayloads)
    {
        var events = new List<Event>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        string currentSettlement = string.Empty;
        var currentIntervals = new List<Interval>();
        var currentStreets = new List<string>();
        var currentBlock = new System.Text.StringBuilder();
        
        foreach (var line in lines)
        {
            if (line.StartsWith("["))
            {
                // Push previous event if exists
                if (!string.IsNullOrEmpty(currentSettlement))
                {
                    events.Add(new Event(currentSettlement, currentStreets.ToList(), currentIntervals.ToList()));
                    territoryPayloads[currentSettlement] = currentBlock.ToString().TrimEnd();
                    currentStreets.Clear();
                    currentIntervals.Clear();
                    currentBlock.Clear();
                }
                
                // e.g. [Місто Старокостянтинів]
                currentSettlement = line.Trim('[', ']');
            }
            
            if (!string.IsNullOrEmpty(currentSettlement))
            {
                currentBlock.AppendLine(line);
            }

            if (line.Contains("| з "))
            {
                var match = Regex.Match(line, @"з (?:(?<s_day>\d+)\s+(?<s_mon>[а-яїєі]+)\s+)?(?<s_h>\d{1,2}):(?<s_m>\d{2})\s+до\s+(?:(?<e_day>\d+)\s+(?<e_mon>[а-яїєі]+)\s+)?(?<e_h>\d{1,2}):(?<e_m>\d{2})");
                if (match.Success)
                {
                    int sh = int.Parse(match.Groups["s_h"].Value);
                    int sm = int.Parse(match.Groups["s_m"].Value);
                    int eh = int.Parse(match.Groups["e_h"].Value);
                    int em = int.Parse(match.Groups["e_m"].Value);
                    
                    var startDt = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, sh, sm, 0);
                    if (match.Groups["s_day"].Success) 
                    {
                        startDt = new DateTime(targetDate.Year, GetMonthIndex(match.Groups["s_mon"].Value), int.Parse(match.Groups["s_day"].Value), sh, sm, 0);
                    }
                    
                    var endDt = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, eh, em, 0);
                    if (match.Groups["e_day"].Success) 
                    {
                        endDt = new DateTime(targetDate.Year, GetMonthIndex(match.Groups["e_mon"].Value), int.Parse(match.Groups["e_day"].Value), eh, em, 0);
                    }
                    else if (endDt < startDt) 
                    {
                        endDt = endDt.AddDays(1); // cross midnight
                    }
                    
                    currentIntervals.Add(new Interval(new DateTimeOffset(startDt, KyivTz.GetUtcOffset(startDt)), new DateTimeOffset(endDt, KyivTz.GetUtcOffset(endDt))));
                }
            }
            else if (line.StartsWith("- "))
            {
                currentStreets.Add(line.Trim());
            }
        }
        
        if (!string.IsNullOrEmpty(currentSettlement))
        {
            events.Add(new Event(currentSettlement, currentStreets, currentIntervals));
            territoryPayloads[currentSettlement] = currentBlock.ToString().TrimEnd();
        }
        
        // Ensure we always have at least one event if the schedule is empty to satisfy the package structure
        if (events.Count == 0 && text.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ"))
        {
             var todayStart = new DateTimeOffset(targetDate, KyivTz.GetUtcOffset(targetDate));
             events.Add(new Event("Система", new List<string> { text.Trim() }, new List<Interval> { new Interval(todayStart, todayStart.AddHours(1)) }));
             territoryPayloads["Система"] = text.Trim();
        }
        
        return events;
    }

    private int GetMonthIndex(string month)
    {
        return month.ToLower() switch
        {
            "січня" => 1,
            "лютого" => 2,
            "березня" => 3,
            "квітня" => 4,
            "травня" => 5,
            "червня" => 6,
            "липня" => 7,
            "серпня" => 8,
            "вересня" => 9,
            "жовтня" => 10,
            "листопада" => 11,
            "грудня" => 12,
            _ => throw new ArgumentException($"Unknown month: {month}")
        };
    }
}
