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
        Assert.Contains("#відключення", caption);
        Assert.Contains("#Старокостянтинів", caption);
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
            new("rosolivetskyi", "Росоловецький старостинський округ", new[] { new OutageRecord("Росоловецький", "АВАРІЙНІ", "з 11:30 до 16:00\nс. Росолівці\nвул. Центральна, 10") }, Array.Empty<OutageRecord>()),
            new("starokostiantyniv", "Місто Старокостянтинів", new[] { new OutageRecord("м. Старокостянтинів", "АВАРІЙНІ", "з 10:15 до 14:00\nвул. Грушевського, 5") }, Array.Empty<OutageRecord>())
        };

        string? result = FacebookContentFormatter.FormatFacebookEmergencyPost("2026-09-15", territories, lastUpdatedUtc: new DateTime(2026, 9, 15, 11, 0, 0, DateTimeKind.Utc));

        Assert.NotNull(result);
        Assert.Contains("АВАРІЙНІ ЗНЕСТРУМЛЕННЯ\n15.09.2026 вівторок, Старокостянтинівська міська територіальна громада", result);
        Assert.Contains("м. Старокостянтинів", result);
        Assert.Contains("10:15–14:00", result);
        Assert.Contains("• вул. Грушевського, 5", result);

        Assert.Contains("СТАРОСТИНСЬКІ ОКРУГИ", result);
        Assert.Contains("РОСОЛОВЕЦЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ", result);
        Assert.Contains("11:30–16:00", result);
        Assert.Contains("с. Росолівці", result);
        Assert.Contains("• вул. Центральна, 10", result);

        // Verify clean typography: zero decorative noise and no dividers
        Assert.DoesNotContain("🚨", result);
        Assert.DoesNotContain("🏙️", result);
        Assert.DoesNotContain("🌾", result);
        Assert.DoesNotContain("━━━━━━━━━━━━━━━━━━━━━━━━━━━━", result);

        // Technical footer
        Assert.DoesNotContain("Технічна інформація:", result);
        Assert.Contains("Останнє оновлення журналу: 14:00", result);
        Assert.Contains("Стан моніторингу: активний", result);
        Assert.EndsWith("#відключення #громада #svitlosk #Старокостянтинів", result.Trim());

        // Verify Starokon appears BEFORE rural districts
        int starokonIndex = result.IndexOf("м. Старокостянтинів");
        int ruralIndex = result.IndexOf("СТАРОСТИНСЬКІ ОКРУГИ");
        Assert.True(starokonIndex < ruralIndex, "Starokostiantyniv must be prioritized before rural districts");
    }

    [Fact]
    public void FormatFacebookPlannedPost_NoPlanned_ShowsStableText()
    {
        var territories = new List<AggregatedTerritoryData>();

        string result = FacebookContentFormatter.FormatFacebookPlannedPost("2026-09-15", territories);

        Assert.Contains("ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n15.09.2026 вівторок, Старокостянтинівська міська територіальна громада", result);
        Assert.Contains("Планові знеструмлення: відсутні", result);
        Assert.Contains("Аварійні знеструмлення: відсутні", result);
        Assert.Contains("планових знеструмлень у громаді не заплановано", result);
        Assert.Contains("Електропостачання споживачів здійснюється у штатному режимі", result);
        Assert.DoesNotContain("Технічна інформація:", result);
        Assert.Contains("#відключення", result);
        Assert.DoesNotContain("⚡", result);
        Assert.DoesNotContain("━━━━━━━━━━━━━━━━━━━━━━━━━━━━", result);
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

        Assert.Contains("ЖУРНАЛ ЗНЕСТРУМЛЕНЬ\n15.09.2026 вівторок, Старокостянтинівська міська територіальна громада", result);
        Assert.Contains("Планові знеструмлення: м. Старокостянтинів, с. Самчики\nАварійні знеструмлення: відсутні", result);
        Assert.Contains("ПЛАНОВІ ЗНЕСТРУМЛЕННЯ", result);
        Assert.Contains("м. Старокостянтинів", result);
        Assert.Contains("10:00–16:00", result);
        Assert.Contains("• вул. Острозького", result);

        Assert.Contains("СТАРОСТИНСЬКІ ОКРУГИ", result);
        Assert.Contains("САМЧИКІВСЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ", result);
        Assert.Contains("09:00–17:00", result);
        Assert.Contains("с. Самчики", result);
        Assert.Contains("• вул. Миру", result);

        Assert.DoesNotContain("Технічна інформація:", result);
        Assert.EndsWith("#відключення #громада #svitlosk #Старокостянтинів", result.Trim());

        Assert.DoesNotContain("⚡", result);
        Assert.DoesNotContain("🏙️", result);
        Assert.DoesNotContain("🌾", result);
        Assert.DoesNotContain("━━━━━━━━━━━━━━━━━━━━━━━━━━━━", result);

        int cityIdx = result.IndexOf("м. Старокостянтинів");
        int ruralIdx = result.IndexOf("СТАРОСТИНСЬКІ ОКРУГИ");
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
        Assert.Contains("ПРОГНОЗ ЗНЕСТРУМЛЕНЬ НА ЗАВТРА\n16.09.2026 середа, Старокостянтинівська міська територіальна громада", result);
        Assert.Contains("м. Старокостянтинів", result);
        Assert.Contains("08:00–12:00", result);
        Assert.Contains("• вул. Франка", result);
        Assert.DoesNotContain("Укренерго", result);
        Assert.DoesNotContain("Технічна інформація:", result);
        Assert.EndsWith("#відключення #громада #svitlosk #Старокостянтинів", result.Trim());
        Assert.DoesNotContain("🔮", result);
        Assert.DoesNotContain("━━━━━━━━━━━━━━━━━━━━━━━━━━━━", result);
    }

    [Fact]
    public void FormatOutageTimeRange_NormalizesVariousFormats()
    {
        Assert.Equal("14:39–17:39", FacebookContentFormatter.FormatOutageTimeRange("з 14:39 до 17:39"));
        Assert.Equal("09:00–20:00", FacebookContentFormatter.FormatOutageTimeRange("з 09:00 до 20:00"));
        Assert.Equal("10:00–14:00", FacebookContentFormatter.FormatOutageTimeRange("10:00 - 14:00"));
        Assert.Equal("до 16:00", FacebookContentFormatter.FormatOutageTimeRange("до 16:00"));
    }

    [Fact]
    public void FormatFacebookPlannedPost_WithDifferentiatedTimes_PutsTimeNextToSettlement()
    {
        var territories = new List<AggregatedTerritoryData>
        {
            new("hubcha", "Губчанський старостинський округ", Array.Empty<OutageRecord>(), new[]
            {
                new OutageRecord("Губчанський", "ПЛАНОВІ", "с. Зеленці | з 09:00 до 13:00\nвул. Бондарчука, 1"),
                new OutageRecord("Губчанський", "ПЛАНОВІ", "с. Губча | з 13:00 до 17:00\nвул. Центральна, 5")
            })
        };

        string result = FacebookContentFormatter.FormatFacebookPlannedPost("2026-09-15", territories);

        Assert.Contains("ГУБЧАНСЬКИЙ СТАРОСТИНСЬКИЙ ОКРУГ", result);
        Assert.Contains("с. Зеленці (09:00–13:00)", result);
        Assert.Contains("• вул. Бондарчука, 1", result);
        Assert.Contains("с. Губча (13:00–17:00)", result);
        Assert.Contains("• вул. Центральна, 5", result);
    }
}
