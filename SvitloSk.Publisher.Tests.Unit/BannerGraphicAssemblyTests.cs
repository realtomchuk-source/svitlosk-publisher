using System;
using System.Text;
using SvitloSk.Publisher.Core.Engine;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit;

public class BannerGraphicAssemblyTests
{
    private readonly BannerGraphicAssembly _assembly = new();

    [Fact]
    public void AssembleDayHeaderSvg_GeneratesValidSvg()
    {
        // Act
        byte[] svgBytes = _assembly.AssembleDayHeaderSvg("2026-09-08");

        // Assert
        Assert.NotNull(svgBytes);
        Assert.True(svgBytes.Length > 0);

        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("ВІВТОРОК", svgText);
        Assert.Contains("08.09.2026", svgText);
        Assert.Contains("ЖУРНАЛ", svgText);
        Assert.Contains("ЗНЕСТРУМЛЕНЬ", svgText);
    }

    [Fact]
    public void AssembleTomorrowHeaderSvg_GeneratesValidSvg()
    {
        // Act
        byte[] svgBytes = _assembly.AssembleTomorrowHeaderSvg("2026-09-09");

        // Assert
        Assert.NotNull(svgBytes);
        Assert.True(svgBytes.Length > 0);

        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("ПРОГНОЗ", svgText);
        Assert.Contains("НА ЗАВТРА", svgText);
        Assert.Contains("СЕРЕДА", svgText);
        Assert.Contains("09.09.2026", svgText);
        Assert.Contains("Старокостянтинівська міська територіальна громада", svgText);
    }

    [Fact]
    public void AssembleNoOutagesSvg_GeneratesValidSvg()
    {
        // Act
        byte[] svgBytes = _assembly.AssembleNoOutagesSvg("2026-09-12");

        // Assert
        Assert.NotNull(svgBytes);
        Assert.True(svgBytes.Length > 0);

        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("ЖУРНАЛ", svgText);
        Assert.Contains("ЗНЕСТРУМЛЕНЬ", svgText);
        Assert.Contains("СУБОТА", svgText);
        Assert.Contains("12.09.2026", svgText);
        Assert.Contains("ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ", svgText);
        Assert.Contains("viewBox=\"0 0 1080 600\"", svgText);
    }

    [Fact]
    public void EditorialContentTransformer_EmptyFeed_ProducesNoOutagesJournalHeaderPackage()
    {
        var transformer = new EditorialContentTransformer();
        var emptyRecords = Array.Empty<OutageRecord>();

        var packages = transformer.TransformFeed(emptyRecords, "12.09.2026");

        Assert.Single(packages);
        Assert.Equal("journal_header", packages[0].TerritoryId);
        Assert.Contains("<b>Планові знеструмлення:</b> відсутні", packages[0].Content);
        Assert.Contains("<b>Аварійні знеструмлення:</b> відсутні", packages[0].Content);
        Assert.DoesNotContain("Планових та аварійних знеструмлень в Старокостянтинівській територіальній громаді не зафіксовано.", packages[0].Content);
    }

    [Fact]
    public void AssembleFacebookDayHeaderSvg_GeneratesFacebook1200x630Svg()
    {
        byte[] svgBytes = _assembly.AssembleFacebookDayHeaderSvg("2026-09-14");

        Assert.NotNull(svgBytes);
        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("viewBox=\"0 0 1200 630\"", svgText);
        Assert.Contains("width=\"1200\"", svgText);
        Assert.Contains("height=\"630\"", svgText);
        Assert.Contains("ПОНЕДІЛОК", svgText);
        Assert.Contains("14.09.2026", svgText);
        Assert.Contains("ЖУРНАЛ", svgText);
        Assert.Contains("ЗНЕСТРУМЛЕНЬ", svgText);
    }

    [Fact]
    public void AssembleFacebookTomorrowHeaderSvg_GeneratesFacebook1200x630Svg()
    {
        byte[] svgBytes = _assembly.AssembleFacebookTomorrowHeaderSvg("2026-09-15");

        Assert.NotNull(svgBytes);
        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("viewBox=\"0 0 1200 630\"", svgText);
        Assert.Contains("width=\"1200\"", svgText);
        Assert.Contains("height=\"630\"", svgText);
        Assert.Contains("ВІВТОРОК", svgText);
        Assert.Contains("15.09.2026", svgText);
        Assert.Contains("ПРОГНОЗ", svgText);
        Assert.Contains("НА ЗАВТРА", svgText);
        Assert.Contains("Старокостянтинівська міська територіальна громада", svgText);
    }

    [Fact]
    public void AssembleFacebookNoOutagesSvg_GeneratesFacebook1200x630Svg()
    {
        byte[] svgBytes = _assembly.AssembleFacebookNoOutagesSvg("2026-09-14");

        Assert.NotNull(svgBytes);
        string svgText = Encoding.UTF8.GetString(svgBytes);
        Assert.Contains("viewBox=\"0 0 1200 630\"", svgText);
        Assert.Contains("width=\"1200\"", svgText);
        Assert.Contains("height=\"630\"", svgText);
        Assert.Contains("ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ", svgText);
    }
}

