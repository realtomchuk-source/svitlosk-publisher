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
}
