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
        Assert.Contains("ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ", svgText);
        Assert.Contains("ПЛАНОВІ ЗНЕСТРУМЛЕННЯ", svgText);
        Assert.Contains("АВАРІЙНІ ЗНЕСТРУМЛЕННЯ", svgText);
        Assert.Contains("НЕ ЗАПЛАНОВАНО", svgText);
        Assert.Contains("НЕ ЗАФІКСОВАНО", svgText);
    }

    [Fact]
    public void EditorialContentTransformer_EmptyFeed_ProducesNoOutagesJournalHeaderPackage()
    {
        var transformer = new EditorialContentTransformer();
        var emptyRecords = Array.Empty<OutageRecord>();

        var packages = transformer.TransformFeed(emptyRecords, "12.09.2026");

        Assert.Single(packages);
        Assert.Equal("journal_header", packages[0].TerritoryId);
        Assert.Contains("Планових та аварійних знеструмлень в Старокостянтинівській територіальній громаді не зафіксовано.", packages[0].Content);
        Assert.Contains("Планові знеструмлення:</b> відсутні", packages[0].Content);
        Assert.Contains("Аварійні знеструмлення:</b> відсутні", packages[0].Content);
    }
}
