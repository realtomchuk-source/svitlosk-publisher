using System;
using System.IO;
using System.Text;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration;

public class BannerRasterizationTests
{
    private readonly BannerGraphicAssembly _assembly = new();
    private readonly SvgSkiaRasterizer _rasterizer = new();

    [Fact]
    public void RasterizeBanners_CreatesValidPngFiles()
    {
        // 1. Day Header Banner
        byte[] daySvg = _assembly.AssembleDayHeaderSvg("2026-09-08");
        byte[] dayPng = _rasterizer.RasterizeSvgToPng(daySvg, 1080, 280);
        Assert.NotNull(dayPng);
        Assert.True(dayPng.Length > 0);

        string artifactDir = @"C:\Users\ATom\.gemini\antigravity\brain\8459d6eb-d4ef-436e-a6fb-5801df454ea5";
        File.WriteAllBytes(Path.Combine(artifactDir, "day_header_banner_preview.png"), dayPng);

        // 2. Tomorrow Forecast Separator Banner
        byte[] tomSvg = _assembly.AssembleTomorrowHeaderSvg("2026-09-09");
        byte[] tomPng = _rasterizer.RasterizeSvgToPng(tomSvg, 1080, 280);
        Assert.NotNull(tomPng);
        Assert.True(tomPng.Length > 0);

        File.WriteAllBytes(Path.Combine(artifactDir, "tomorrow_banner_preview.png"), tomPng);
    }
}
