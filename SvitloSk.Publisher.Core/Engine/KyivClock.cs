using System;

namespace SvitloSk.Publisher.Core.Engine;

/// <summary>
/// Single source of Kyiv (Ukraine) local time for the whole publisher.
/// Uses the system time zone database (IANA "Europe/Kyiv" or Windows "FLE Standard Time"),
/// so daylight saving transitions (last Sunday of March / October) are handled automatically.
/// Replaces the former hard-coded "UTC + 3 hours" offset, which was wrong during winter time (UTC+2).
/// </summary>
public static class KyivClock
{
    private static readonly TimeZoneInfo KyivZone = ResolveKyivZone();

    /// <summary>Kyiv time zone in use.</summary>
    public static TimeZoneInfo Zone => KyivZone;

    /// <summary>Optional override of current UTC time (for tests).</summary>
    public static Func<DateTime>? UtcNowOverride { get; set; }

    /// <summary>Current UTC time.</summary>
    public static DateTime UtcNow => UtcNowOverride?.Invoke() ?? DateTime.UtcNow;

    /// <summary>Current Kyiv local time.</summary>
    public static DateTime Now => ToKyiv(UtcNow);

    /// <summary>Current Kyiv calendar date in yyyy-MM-dd format.</summary>
    public static string TodayIso => Now.ToString("yyyy-MM-dd");

    /// <summary>
    /// Converts a UTC timestamp to Kyiv local time. Values with Unspecified kind are treated as UTC.
    /// Values already marked as Local are converted from the machine local zone.
    /// </summary>
    public static DateTime ToKyiv(DateTime utcTime)
    {
        DateTime utc = utcTime.Kind switch
        {
            DateTimeKind.Utc => utcTime,
            DateTimeKind.Local => utcTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcTime, DateTimeKind.Utc)
        };

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, KyivZone), DateTimeKind.Unspecified);
    }

    private static TimeZoneInfo ResolveKyivZone()
    {
        foreach (var id in new[] { "Europe/Kyiv", "Europe/Kiev", "FLE Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        // Last-resort fallback: EET/EEST rules (UTC+2, DST from last Sunday of March 03:00 to last Sunday of October 04:00 local).
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 4, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Kyiv-Fallback", TimeSpan.FromHours(2), "Kyiv (fallback)", "EET", "EEST", new[] { rule });
    }
}
