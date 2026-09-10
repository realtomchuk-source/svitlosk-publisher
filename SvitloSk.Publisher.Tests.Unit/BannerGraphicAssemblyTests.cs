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
        Assert.Contains("ЖУРНАЛ ЗНЕСТРУМЛЕНЬ", svgText);
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
        Assert.Contains("ПРОГНОЗ НА ЗАВТРА", svgText);
        Assert.Contains("СЕРЕДА", svgText);
        Assert.Contains("09.09.2026", svgText);
    }
}
