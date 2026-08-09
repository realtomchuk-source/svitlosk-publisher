using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core;

public static class EventHashGenerator
{
    public static string GenerateHash(IEnumerable<Event> events, DateTimeOffset windowStart, DateTimeOffset windowEnd)
    {
        var eventStrings = new List<string>();

        foreach (var ev in events)
        {
            var overlappingIntervals = ev.Intervals
                .Where(i => i.StartTime != i.EndTime && i.StartTime < windowEnd && i.EndTime > windowStart)
                .OrderBy(i => i.StartTime)
                .ThenBy(i => i.EndTime)
                .ToList();
                
            if (!overlappingIntervals.Any()) continue;
            
            var sb = new StringBuilder();
            sb.Append(ev.Settlement).Append('|');
            
            if (ev.Streets != null)
            {
                var sortedStreets = ev.Streets.OrderBy(s => s, StringComparer.Ordinal);
                foreach (var street in sortedStreets)
                {
                    sb.Append(street).Append(',');
                }
            }
            sb.Append('|');
            
            foreach (var interval in overlappingIntervals)
            {
                sb.Append(interval.StartTime.ToUnixTimeSeconds()).Append('-').Append(interval.EndTime.ToUnixTimeSeconds()).Append(';');
            }
            
            eventStrings.Add(sb.ToString());
        }
        
        eventStrings.Sort(StringComparer.Ordinal);
        
        var finalSb = new StringBuilder();
        foreach (var evStr in eventStrings)
        {
            finalSb.Append(evStr).Append("||");
        }
        
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(finalSb.ToString()));
        return Convert.ToBase64String(bytes);
    }
}
