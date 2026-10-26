using System;
using SvitloSk.Publisher.Core.Engine;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit;

public class KyivClockTests
{
    [Theory]
    // Summer time (EEST, UTC+3)
    [InlineData("2026-10-18T09:00:00Z", "2026-10-18 12:00")]
    // Last moment of summer time: 25.10.2026 00:59 UTC = 03:59 EEST
    [InlineData("2026-10-25T00:59:00Z", "2026-10-25 03:59")]
    // After transition: 25.10.2026 01:00 UTC = 03:00 EET (UTC+2)
    [InlineData("2026-10-25T01:00:00Z", "2026-10-25 03:00")]
    // Winter time (EET, UTC+2)
    [InlineData("2026-10-25T10:00:00Z", "2026-10-25 12:00")]
    [InlineData("2026-12-31T22:30:00Z", "2027-01-01 00:30")]
    // Spring transition: 29.03.2026 00:59 UTC = 02:59 EET, 01:00 UTC = 04:00 EEST
    [InlineData("2026-03-29T00:59:00Z", "2026-03-29 02:59")]
    [InlineData("2026-03-29T01:00:00Z", "2026-03-29 04:00")]
    public void ToKyiv_HandlesDaylightSavingTransitions(string utcIso, string expectedKyiv)
    {
        var utc = DateTime.Parse(utcIso, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);

        Assert.Equal(expectedKyiv, KyivClock.ToKyiv(utc).ToString("yyyy-MM-dd HH:mm"));
    }

    [Fact]
    public void ToKyiv_TreatsUnspecifiedKindAsUtc()
    {
        var unspecified = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Unspecified);
        Assert.Equal(12, KyivClock.ToKyiv(unspecified).Hour);
    }

    [Fact]
    public void DayBoundary_InWinter_SwitchesAtKyivMidnight()
    {
        try
        {
            // 21:59 UTC on 01.11 = 23:59 Kyiv (still 01.11)
            KyivClock.UtcNowOverride = () => new DateTime(2026, 11, 1, 21, 59, 0, DateTimeKind.Utc);
            Assert.Equal("2026-11-01", KyivClock.TodayIso);

            // 22:00 UTC on 01.11 = 00:00 Kyiv on 02.11
            KyivClock.UtcNowOverride = () => new DateTime(2026, 11, 1, 22, 0, 0, DateTimeKind.Utc);
            Assert.Equal("2026-11-02", KyivClock.TodayIso);
        }
        finally
        {
            KyivClock.UtcNowOverride = null;
        }
    }

    [Fact]
    public void ForecastGate_InWinter_OpensAtKyivNoon()
    {
        // 10:00 UTC in winter = 12:00 Kyiv (former +3 logic would give 13:00 and open the gate at 11:00 Kyiv)
        Assert.Equal(12, KyivClock.ToKyiv(new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc)).Hour);
        Assert.Equal(11, KyivClock.ToKyiv(new DateTime(2026, 11, 2, 9, 59, 0, DateTimeKind.Utc)).Hour);
    }
}
