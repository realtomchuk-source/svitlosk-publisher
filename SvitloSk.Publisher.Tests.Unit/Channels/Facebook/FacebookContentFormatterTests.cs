using System;
using System.Collections.Generic;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.Facebook;

public class FacebookContentFormatterTests
{
    [Fact]
    public void StripHtml_RemovesHtmlTagsAndDecodesEntities()
    {
        string input = "<b>Планові знеструмлення:</b> м. Старокостянтинів &amp; села";
        string result = FacebookContentFormatter.StripHtml(input);

        Assert.Equal("Планові знеструмлення: м. Старокостянтинів & села", result);
        Assert.DoesNotContain("<b>", result);
        Assert.DoesNotContain("</b>", result);
    }

    [Fact]
    public void FormatGraphicCaption_FormatsDateAndHashtags()
    {
        string caption = FacebookContentFormatter.FormatGraphicCaption("14.09.2026");

        Assert.Contains("14.09.2026", caption);
        Assert.Contains("12-підчерговий графік", caption);
        Assert.Contains("#графік", caption);
        Assert.Contains("#старокостянтинів", caption);
    }

    [Fact]
    public void FormatConsolidatedTodayPost_WithOutages_ProducesCleanFormattedPost()
    {
        var packages = new List<TransformedPackage>
        {
            new TransformedPackage("journal_header", "<b>Планові:</b> є", null, true),
            new TransformedPackage("starokostiantyniv", "<b>м. Старокостянтинів</b>\n• 10:00 - 14:00 (1 черга)", null, true),
            new TransformedPackage("kapustynskyi", "<b>Капустинський старостат</b>\n• 12:00 - 16:00 (2 черга)", null, true)
        };

        string post = FacebookContentFormatter.FormatConsolidatedTodayPost("2026-09-14", packages, new DateTime(2026, 9, 14, 11, 30, 0, DateTimeKind.Utc));

        Assert.Contains("ЖУРНАЛ ЗНЕСТРУМЛЕНЬ", post);
        Assert.Contains("14.09.2026", post);
        Assert.Contains("м. Старокостянтинів", post);
        Assert.Contains("Капустинський старостат", post);
        Assert.Contains("Останнє оновлення: 14:30", post);
        Assert.DoesNotContain("<b>", post);
        Assert.DoesNotContain("</b>", post);
    }

    [Fact]
    public void FormatConsolidatedTodayPost_NoOutages_ShowsStableStatus()
    {
        var packages = new List<TransformedPackage>();

        string post = FacebookContentFormatter.FormatConsolidatedTodayPost("2026-09-14", packages);

        Assert.Contains("Електропостачання стабільне", post);
    }

    [Fact]
    public void FormatConsolidatedTomorrowPost_IncludesTomorrowTerritories()
    {
        var packages = new List<TransformedPackage>
        {
            new TransformedPackage("tomorrow_separator", "Прогноз", null, false),
            new TransformedPackage("tomorrow_starokostiantyniv", "<b>м. Старокостянтинів</b>\n• 08:00 - 12:00 (1 черга)", null, false)
        };

        string post = FacebookContentFormatter.FormatConsolidatedTomorrowPost("2026-09-15", packages);

        Assert.Contains("ПРОГНОЗ ВІДКЛЮЧЕНЬ НА ЗАВТРА", post);
        Assert.Contains("15.09.2026", post);
        Assert.Contains("м. Старокостянтинів", post);
        Assert.DoesNotContain("<b>", post);
    }

    [Fact]
    public void FormatFacebookEmergencyPost_NoEmergencies_ReturnsNull()
    {
        var territories = new List<AggregatedTerritoryData>
        {
            new("starokostiantyniv", "Місто Старокостянтинів", Array.Empty<OutageRecord>(), new[] { new OutageRecord("м. Старокостянтинів", "ПЛАНОВІ", "09:00 - 17:00\nвул. Миру") })
        };

        string? result = FacebookContentFormatter.FormatFacebookEmergencyPost("2026-09-15", territories);
        Assert.Null(result);
    }

    [Fact]
    public void FormatFacebookEmergencyPost_WithEmergencies_FormatsCityFirstAndDistricts()
    {
        var territories = new List<AggregatedTerritoryData>
        {
            new("rosolivetskyi", "Росоловецький старостинський округ", new[] { new OutageRecord("Росоловецький", "АВАРІЙНІ", "до 16:00\nвул. Центральна, 10") }, Array.Empty<OutageRecord>()),
            new("starokostiantyniv", "Місто Старокостянтинів", new[] { new OutageRecord("м. Старокостянтинів", "АВАРІЙНІ", "до 14:00\nвул. Грушевського, 5") }, Array.Empty<OutageRecord>())
        };

        string? result = FacebookContentFormatter.FormatFacebookEmergencyPost("2026-09-15", territories, lastUpdatedUtc: new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(result);
        Assert.Contains("🚨 АВАРІЙНІ ЗНЕСТРУМЛЕННЯ — 15.09.2026", result);
        Assert.Contains("🏙️ МІСТО СТАРОКОСТЯНТИНІВ", result);
        Assert.Contains("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ", result);
        Assert.Contains("РОСОЛОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ", result);
        Assert.Contains("Час оновлення: 14:00 (Київ)", result);
        Assert.Contains("#аварійнівідключення", result);

        // Verify Starokon appears BEFORE rural districts
        int starokonIndex = result.IndexOf("🏙️ МІСТО СТАРОКОСТЯНТИНІВ");
        int ruralIndex = result.IndexOf("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ");
        Assert.True(starokonIndex < ruralIndex, "Starokostiantyniv must be prioritized before rural districts");
    }

    [Fact]
    public void FormatFacebookPlannedPost_NoPlanned_ShowsStableText()
    {
        var territories = new List<AggregatedTerritoryData>();

        string result = FacebookContentFormatter.FormatFacebookPlannedPost("2026-09-15", territories);

        Assert.Contains("⚡ ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 15.09.2026", result);
        Assert.Contains("планових знеструмлень у громаді не заплановано", result);
        Assert.Contains("Електропостачання споживачів здійснюється у штатному режимі", result);
    }

    [Fact]
    public void FormatFacebookPlannedPost_WithPlanned_FormatsCityFirst()
    {
        var territories = new List<AggregatedTerritoryData>
        {
            new("samchyky", "Самчиківський старостинський округ", Array.Empty<OutageRecord>(), new[] { new OutageRecord("Самчиківський", "ПЛАНОВІ", "09:00 - 17:00\nс. Самчики\nвул. Миру") }),
            new("starokostiantyniv", "Місто Старокостянтинів", Array.Empty<OutageRecord>(), new[] { new OutageRecord("м. Старокостянтинів", "ПЛАНОВІ", "10:00 - 16:00\nвул. Острозького") })
        };

        string result = FacebookContentFormatter.FormatFacebookPlannedPost("2026-09-15", territories);

        Assert.Contains("⚡ ПЛАНОВІ ЗНЕСТРУМЛЕННЯ — 15.09.2026", result);
        Assert.Contains("🏙️ МІСТО СТАРОКОСТЯНТИНІВ", result);
        Assert.Contains("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ", result);
        Assert.Contains("САМЧИКІВСЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ", result);

        int cityIdx = result.IndexOf("🏙️ МІСТО СТАРОКОСТЯНТИНІВ");
        int ruralIdx = result.IndexOf("🌾 СТАРОСТИНСЬКІ ОКРУГИ ГРОМАДИ");
        Assert.True(cityIdx < ruralIdx);
    }

    [Fact]
    public void FormatFacebookTomorrowPost_WithTomorrowData_FormatsStructuredForecast()
    {
        var territories = new List<AggregatedTerritoryData>
        {
            new("starokostiantyniv", "Місто Старокостянтинів", Array.Empty<OutageRecord>(), new[] { new OutageRecord("м. Старокостянтинів", "ПЛАНОВІ", "08:00 - 12:00\nвул. Франка") })
        };

        string? result = FacebookContentFormatter.FormatFacebookTomorrowPost("2026-09-16", territories);

        Assert.NotNull(result);
        Assert.Contains("🔮 ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА — 16.09.2026", result);
        Assert.Contains("🏙️ МІСТО СТАРОКОСТЯНТИНІВ", result);
        Assert.Contains("Укренерго", result);
        Assert.Contains("#прогноз", result);
    }
}
