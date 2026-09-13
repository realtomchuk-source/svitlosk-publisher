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
        byte[] dayPng = _rasterizer.RasterizeSvgToPng(daySvg, 1080, 480);
        Assert.NotNull(dayPng);
        Assert.True(dayPng.Length > 0);

        string artifactDir = @"C:\Users\ATom\.gemini\antigravity\brain\5c319537-8b33-4b99-945b-4b3866e21113";
        if (Directory.Exists(artifactDir))
        {
            File.WriteAllBytes(Path.Combine(artifactDir, "day_header_banner_preview.png"), dayPng);
        }

        // 2. Tomorrow Forecast Separator Banner
        byte[] tomSvg = _assembly.AssembleTomorrowHeaderSvg("2026-09-09");
        byte[] tomPng = _rasterizer.RasterizeSvgToPng(tomSvg, 1080, 480);
        Assert.NotNull(tomPng);
        Assert.True(tomPng.Length > 0);

        if (Directory.Exists(artifactDir))
        {
            File.WriteAllBytes(Path.Combine(artifactDir, "tomorrow_banner_preview.png"), tomPng);
        }

        // 3. No Outages Rectangle Banner (1080x600)
        byte[] noOutagesSvg = _assembly.AssembleNoOutagesSvg("2026-09-12");
        byte[] noOutagesPng = _rasterizer.RasterizeSvgToPng(noOutagesSvg, 1080, 600);
        Assert.NotNull(noOutagesPng);
        Assert.True(noOutagesPng.Length > 0);

        if (Directory.Exists(artifactDir))
        {
            File.WriteAllBytes(Path.Combine(artifactDir, "no_outages_banner_preview.png"), noOutagesPng);
        }
    }
}
