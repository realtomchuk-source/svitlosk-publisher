using System;
using System.IO;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Infrastructure.Graphics;

/// <summary>
/// Core vector graphics rendering engine for SvitloSk Publisher.
/// Rasterizes SVG content (12-subqueue grids, horizontal and square banners)
/// into crisp, production-grade PNG byte streams using SkiaSharp.
/// Platform-agnostic: shared across all publication channels (Telegram, Facebook, Web).
/// </summary>
public class SvgSkiaRasterizer : IGraphicRasterizer
{
    public byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 1000, int height = 650)
    {
        if (svgBytes == null || svgBytes.Length == 0)
            throw new ArgumentException("SVG byte payload cannot be null or empty.", nameof(svgBytes));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        using var ms = new MemoryStream(svgBytes);
        using var svg = new Svg.Skia.SKSvg();

        try
        {
            var picture = svg.Load(ms);
            if (picture == null || svg.Picture == null)
            {
                throw new InvalidOperationException("Failed to load and parse SVG picture data.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Malformed or unparseable SVG content: {ex.Message}", ex);
        }

        var imageInfo = new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Premul);
        using var surface = SkiaSharp.SKSurface.Create(imageInfo);
        if (surface == null)
        {
            throw new InvalidOperationException($"Failed to allocate SKSurface of dimensions {width}x{height}.");
        }

        var canvas = surface.Canvas;
        canvas.Clear(SkiaSharp.SKColors.Transparent);

        float scaleX = width / svg.Picture.CullRect.Width;
        float scaleY = height / svg.Picture.CullRect.Height;
        var matrix = SkiaSharp.SKMatrix.CreateScale(scaleX, scaleY);

        canvas.DrawPicture(svg.Picture, ref matrix);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
