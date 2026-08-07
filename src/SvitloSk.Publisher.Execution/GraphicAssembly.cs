// Source: GRAPHIC_ASSEMBLY_SPECIFICATION.md
// Section: 02

using System;
using System.Linq;
using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public class GraphicAssembly : IGraphicAssembly
{
    public GraphicArtifact Assemble(GraphicInputPackage inputPackage, EditorialDecision decision)
    {
        if (inputPackage == null) throw new ArgumentNullException(nameof(inputPackage));
        if (decision == null) throw new ArgumentNullException(nameof(decision));

        if (inputPackage.Queues == null || inputPackage.Queues.Count != 6)
        {
            throw new InvalidOperationException("Graphic Assembly requires exactly 6 root queues.");
        }

        if (inputPackage.Queues.Any(q => q.Subqueues == null || q.Subqueues.Count != 2))
        {
            throw new InvalidOperationException("Graphic Assembly requires exactly 2 subqueues per root queue.");
        }

        var layoutModel = LayoutEngine.CalculateLayout(inputPackage);
        
        string logoBase64 = LoadLogoFromReferenceAssets();
        
        return SvgRenderer.Render(layoutModel, logoBase64, inputPackage);
    }

    private string LoadLogoFromReferenceAssets()
    {
        // Find reference/og-image.png by searching upwards from BaseDirectory
        string? currentDir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(currentDir))
        {
            string possiblePath = System.IO.Path.Combine(currentDir, "reference", "og-image.png");
            if (System.IO.File.Exists(possiblePath))
            {
                byte[] bytes = System.IO.File.ReadAllBytes(possiblePath);
                return "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            currentDir = System.IO.Path.GetDirectoryName(currentDir);
        }
        return ""; // Fallback if not found
    }
}

// ---------------------------------------------------------
// Semantic Layout Model
// ---------------------------------------------------------

public class GraphicLayoutModel
{
    public int Width { get; set; }
    public int Height { get; set; }
    public HeaderRegion LayoutHeader { get; set; } = new();
    public TimelineRegion LayoutTimeline { get; set; } = new();
    public System.Collections.Generic.List<QueueRegion> LayoutQueues { get; set; } = new();
    public FooterRegion LayoutFooter { get; set; } = new();
}

public class RegionBase
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class HeaderRegion : RegionBase { }
public class TimelineRegion : RegionBase { }
public class QueueRegion : RegionBase 
{ 
    public string QueueId { get; set; } = string.Empty;
    public System.Collections.Generic.List<SubqueueRegion> Subqueues { get; set; } = new();
}
public class SubqueueRegion : RegionBase 
{ 
    public string SubqueueId { get; set; } = string.Empty;
    public System.Collections.Generic.List<IntervalRegion> Intervals { get; set; } = new();
}
public class IntervalRegion : RegionBase 
{ 
    public string Color { get; set; } = string.Empty;
}
public class FooterRegion : RegionBase { }

// ---------------------------------------------------------
// Layout Engine
// ---------------------------------------------------------

public static class LayoutEngine
{
    public static GraphicLayoutModel CalculateLayout(GraphicInputPackage inputPackage)
    {
        var model = new GraphicLayoutModel();
        model.Width = 800;
        int headerHeight = 100;
        int timelineHeight = 40;
        int queueHeight = 80;
        int footerHeight = 40;
        
        model.Height = headerHeight + timelineHeight + (6 * queueHeight) + footerHeight;

        model.LayoutHeader = new HeaderRegion { X = 0, Y = 0, Width = model.Width, Height = headerHeight };
        model.LayoutTimeline = new TimelineRegion { X = 0, Y = headerHeight, Width = model.Width, Height = timelineHeight };

        int currentY = headerHeight + timelineHeight;
        int timelineStartX = 100;
        int timelineWidth = model.Width - timelineStartX - 20;

        foreach (var queue in inputPackage.Queues)
        {
            var qRegion = new QueueRegion 
            { 
                X = 0, Y = currentY, Width = model.Width, Height = queueHeight, 
                QueueId = queue.QueueId 
            };
            
            int subY = 5;
            int subHeight = (queueHeight - 20) / 2;
            foreach (var subqueue in queue.Subqueues)
            {
                var sqRegion = new SubqueueRegion
                {
                    X = timelineStartX, Y = subY, Width = timelineWidth, Height = subHeight,
                    SubqueueId = subqueue.SubqueueId
                };

                foreach (var interval in subqueue.Intervals)
                {
                    double startRatio = ParseTimeRatio(interval.StartTime);
                    double endRatio = ParseTimeRatio(interval.EndTime);
                    double xPos = startRatio * timelineWidth;
                    double w = (endRatio - startRatio) * timelineWidth;
                    string color = interval.Status.Equals("restricted", StringComparison.OrdinalIgnoreCase) ? "#EE7221" : "#374151";

                    sqRegion.Intervals.Add(new IntervalRegion { X = xPos, Y = 0, Width = w, Height = subHeight, Color = color });
                }
                
                qRegion.Subqueues.Add(sqRegion);
                subY += subHeight + 10;
            }
            
            model.LayoutQueues.Add(qRegion);
            currentY += queueHeight;
        }

        model.LayoutFooter = new FooterRegion { X = 0, Y = currentY, Width = model.Width, Height = footerHeight };

        return model;
    }

    private static double ParseTimeRatio(string timeString)
    {
        if (string.IsNullOrWhiteSpace(timeString)) return 0;
        var parts = timeString.Split(':');
        if (parts.Length != 2) return 0;
        if (int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes))
        {
            return (hours * 60.0 + minutes) / 1440.0;
        }
        return 0;
    }
}

// ---------------------------------------------------------
// SVG Renderer
// ---------------------------------------------------------

public static class SvgRenderer
{
    public static GraphicArtifact Render(GraphicLayoutModel layout, string logoBase64, GraphicInputPackage inputPackage)
    {
        var svgBuilder = new System.Text.StringBuilder();

        // 2. Add SVG viewBox and preserve width/height. Root <svg> only has font-family="Inter, sans-serif"
        svgBuilder.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {layout.Width} {layout.Height}\" width=\"{layout.Width}\" height=\"{layout.Height}\" font-family=\"Inter, sans-serif\">");

        svgBuilder.AppendLine($"  <rect width=\"{layout.Width}\" height=\"{layout.Height}\" fill=\"#FFFFFF\" />");

        // HeaderRegion
        svgBuilder.AppendLine($"  <g id=\"HeaderRegion\" transform=\"translate({layout.LayoutHeader.X}, {layout.LayoutHeader.Y})\">");
        svgBuilder.AppendLine($"    <rect width=\"{layout.LayoutHeader.Width}\" height=\"{layout.LayoutHeader.Height}\" fill=\"#EE7221\" />");
        if (!string.IsNullOrEmpty(logoBase64))
        {
            svgBuilder.AppendLine($"    <image href=\"{logoBase64}\" x=\"20\" y=\"20\" width=\"150\" height=\"50\" />");
        }
        svgBuilder.AppendLine($"    <text x=\"20\" y=\"80\" fill=\"#FFFFFF\" font-size=\"16\">ГРАФІК ЗНЕСТРУМЛЕНЬ: {System.Net.WebUtility.HtmlEncode(inputPackage.TerritorialScope)}</text>");
        svgBuilder.AppendLine($"    <text x=\"{layout.LayoutHeader.Width - 20}\" y=\"40\" fill=\"#FFFFFF\" font-size=\"14\" text-anchor=\"end\">на {System.Net.WebUtility.HtmlEncode(inputPackage.Metadata.TargetDate)}</text>");
        svgBuilder.AppendLine($"    <text x=\"{layout.LayoutHeader.Width - 20}\" y=\"80\" fill=\"#FFFFFF\" font-size=\"12\" text-anchor=\"end\">Сформовано: {System.Net.WebUtility.HtmlEncode(inputPackage.Metadata.GenerationTimestamp)}</text>");
        svgBuilder.AppendLine($"  </g>");

        // TimelineRegion
        svgBuilder.AppendLine($"  <g id=\"TimelineRegion\" transform=\"translate({layout.LayoutTimeline.X}, {layout.LayoutTimeline.Y})\">");
        svgBuilder.AppendLine($"    <rect width=\"{layout.LayoutTimeline.Width}\" height=\"{layout.LayoutTimeline.Height}\" fill=\"#374151\" />");
        svgBuilder.AppendLine($"    <text x=\"20\" y=\"25\" fill=\"#FFFFFF\" font-size=\"14\">00:00 - 24:00 Axis</text>");
        svgBuilder.AppendLine($"  </g>");

        // QueueRegions
        foreach (var queue in layout.LayoutQueues)
        {
            svgBuilder.AppendLine($"  <g id=\"QueueRegion_{queue.QueueId}\" transform=\"translate({queue.X}, {queue.Y})\">");
            
            svgBuilder.AppendLine($"    <rect width=\"80\" height=\"{queue.Height - 10}\" fill=\"#EE7221\" x=\"10\" y=\"5\" rx=\"4\" />");
            svgBuilder.AppendLine($"    <text x=\"50\" y=\"{queue.Height / 2 + 5}\" fill=\"#FFFFFF\" font-size=\"20\" font-weight=\"bold\" text-anchor=\"middle\">{System.Net.WebUtility.HtmlEncode(queue.QueueId)}</text>");
            
            foreach (var subqueue in queue.Subqueues)
            {
                svgBuilder.AppendLine($"    <g id=\"SubqueueRegion_{subqueue.SubqueueId}\" transform=\"translate({subqueue.X}, {subqueue.Y})\">");
                
                foreach (var interval in subqueue.Intervals)
                {
                    svgBuilder.AppendLine($"      <rect x=\"{interval.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{interval.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" width=\"{interval.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" height=\"{interval.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" fill=\"{interval.Color}\" rx=\"2\" />");
                }
                
                svgBuilder.AppendLine($"    </g>");
            }
            svgBuilder.AppendLine($"  </g>");
        }

        // FooterRegion
        svgBuilder.AppendLine($"  <g id=\"FooterRegion\" transform=\"translate({layout.LayoutFooter.X}, {layout.LayoutFooter.Y})\">");
        svgBuilder.AppendLine($"    <rect width=\"{layout.LayoutFooter.Width}\" height=\"{layout.LayoutFooter.Height}\" fill=\"#FFFFFF\" />");
        svgBuilder.AppendLine($"    <text x=\"20\" y=\"25\" fill=\"#374151\" font-size=\"12\">Reserved for future specification</text>");
        svgBuilder.AppendLine($"  </g>");

        svgBuilder.AppendLine("</svg>");

        byte[] payload = System.Text.Encoding.UTF8.GetBytes(svgBuilder.ToString());
        return new GraphicArtifact(payload, "SVG");
    }
}
