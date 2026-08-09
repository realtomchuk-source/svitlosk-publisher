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

namespace SvitloSk.Publisher.Runtime;

public class RealOutagesSkInputPackageProvider : IInputPackageProvider
{
    private readonly HttpClient _httpClient;
    private readonly OutagesSkOptions _options;
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
        IOptions<OutagesSkOptions> options,
        ILogger<RealOutagesSkInputPackageProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    private class SnapshotDto
    {
        [JsonPropertyName("events")]
        public List<EventDto>? Events { get; set; }
    }

    private class EventDto
    {
        [JsonPropertyName("settlement")]
        public string? Settlement { get; set; }
        
        [JsonPropertyName("streets")]
        public List<string>? Streets { get; set; }
        
        [JsonPropertyName("intervals")]
        public List<IntervalDto>? Intervals { get; set; }
    }

    private class IntervalDto
    {
        [JsonPropertyName("start")]
        public string? Start { get; set; }
        
        [JsonPropertyName("end")]
        public string? End { get; set; }
    }

    public async Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
    {
        var events = new List<Event>();
        
        try
        {
            var jsonContent = await _httpClient.GetStringAsync(_options.SnapshotUrl, cancellationToken);
            
            var snapshot = JsonSerializer.Deserialize<SnapshotDto>(jsonContent);
            
            if (snapshot?.Events != null)
            {
                foreach (var ev in snapshot.Events)
                {
                    if (string.IsNullOrWhiteSpace(ev.Settlement)) continue;
                    
                    var parsedIntervals = new List<Interval>();
                    
                    if (ev.Intervals != null)
                    {
                        foreach (var intervalDto in ev.Intervals)
                        {
                            if (TryParseTimestamp(intervalDto.Start, out var startOffset) && 
                                TryParseTimestamp(intervalDto.End, out var endOffset))
                            {
                                if (startOffset >= endOffset)
                                {
                                    throw new InvalidOperationException($"Invalid interval: StartTime {startOffset} must be before EndTime {endOffset}");
                                }
                                parsedIntervals.Add(new Interval(startOffset, endOffset));
                            }
                        }
                    }
                    
                    events.Add(new Event(
                        ev.Settlement,
                        ev.Streets ?? new List<string>(),
                        parsedIntervals
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch or parse real OutagesSk JSON snapshot.");
            throw; // Re-throw to prevent generating corrupted empty packages
        }

        return new InputPackage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "RealOutagesSk",
            "Starokostiantyniv Urban Territorial Community",
            events
        );
    }
    
    private bool TryParseTimestamp(string? raw, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        
        // Expected format: dd.MM.yyyyHH:mm
        if (DateTime.TryParseExact(raw, "dd.MM.yyyyHH:mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            // The parsed time is in Kyiv time. We must convert it to DateTimeOffset correctly.
            var offset = KyivTz.GetUtcOffset(parsed);
            result = new DateTimeOffset(parsed, offset);
            return true;
        }
        
        return false;
    }
}
