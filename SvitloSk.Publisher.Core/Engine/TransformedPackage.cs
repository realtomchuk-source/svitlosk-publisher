using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Engine;

public record TransformedPackage(
    string TerritoryId,
    string Content,
    byte[]? GraphicBytes,
    bool IsPersistent
);

public record GraphicInterval(
    string StartTime,
    string EndTime,
    string Status
)
{
    public (TimeSpan Start, TimeSpan End) ParseTimes()
    {
        if (string.IsNullOrWhiteSpace(StartTime) || !TimeSpan.TryParse(StartTime, out var start))
            throw new FormatException($"Invalid StartTime format: '{StartTime}'. Expected HH:mm.");

        TimeSpan end;
        if (EndTime == "24:00")
        {
            end = TimeSpan.FromHours(24);
        }
        else if (string.IsNullOrWhiteSpace(EndTime) || !TimeSpan.TryParse(EndTime, out end))
        {
            throw new FormatException($"Invalid EndTime format: '{EndTime}'. Expected HH:mm.");
        }

        if (start < TimeSpan.Zero || start > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(StartTime), "StartTime must be between 00:00 and 24:00.");

        if (end < TimeSpan.Zero || end > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(EndTime), "EndTime must be between 00:00 and 24:00.");

        if (start >= end)
            throw new ArgumentException($"StartTime ({StartTime}) must be strictly earlier than EndTime ({EndTime}).");

        return (start, end);
    }
}

public record SubqueueSchedule(
    string SubqueueId,
    IReadOnlyList<GraphicInterval> Intervals
);

public record QueueSchedule(
    string QueueId,
    IReadOnlyList<SubqueueSchedule> Subqueues
);

public record GraphicMetadata(
    string PackageId,
    string GenerationTimestamp,
    string TargetDate,
    string SourceIdentifier
);

public record GraphicInputPackage(
    GraphicMetadata Metadata,
    string TerritorialScope,
    IReadOnlyList<QueueSchedule> Queues
);

public interface IGraphicAssembly
{
    byte[] AssembleSvg(GraphicInputPackage inputPackage);
}

public class GraphicAssembly : IGraphicAssembly
{
    // Canonical corporate colors as defined in GRAPHIC_ASSEMBLY_SPECIFICATION.md and TJS-022
    public const string OutageColor = "#FF8C00"; // Orange / Restricted
    public const string PossibleColor = "#708090"; // Gray / Possible restriction
    public const string PoweredColor = "#2E8B57"; // Green / Powered (or background canvas)
    public const string BackgroundColor = "#1E1E1E"; // Dark canvas background
    public const string TextColor = "#FFFFFF";
    public const string GridLineColor = "#333333";

    public byte[] AssembleSvg(GraphicInputPackage inputPackage)
    {
        if (inputPackage == null)
            throw new ArgumentNullException(nameof(inputPackage));

        ValidatePackage(inputPackage);

        // SVG Layout Geometry (Deterministic Canvas: 1000 x 650)
        int canvasWidth = 1000;
        int canvasHeight = 650;

        int headerHeight = 90;
        int timelineHeaderHeight = 35;
        int timelineLeft = 140;
        int timelineWidth = 820;
        int timelineTop = headerHeight + 10;
        
        int gridTop = timelineTop + timelineHeaderHeight;
        int queueRowHeight = 75; // for 2 subqueues: 32px each + padding

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{BackgroundColor}\"/>");

        // Header Region
        sb.AppendLine("  <!-- HeaderRegion -->");
        sb.AppendLine("  <g id=\"headerRegion\">");
        sb.AppendLine($"    <text x=\"30\" y=\"40\" font-family=\"Arial, sans-serif\" font-size=\"22\" font-weight=\"bold\" fill=\"{OutageColor}\">⚡ SvitloSk | Графік знеструмлень</text>");
        sb.AppendLine($"    <text x=\"30\" y=\"65\" font-family=\"Arial, sans-serif\" font-size=\"14\" fill=\"{TextColor}\">Територія: {EscapeXml(inputPackage.TerritorialScope)} | Дата: {EscapeXml(inputPackage.Metadata.TargetDate)}</text>");
        sb.AppendLine($"    <text x=\"{canvasWidth - 30}\" y=\"65\" font-family=\"Arial, sans-serif\" font-size=\"12\" text-anchor=\"end\" fill=\"#AAAAAA\">Оновлено: {EscapeXml(inputPackage.Metadata.GenerationTimestamp)}</text>");
        sb.AppendLine("  </g>");

        // Timeline Axis Region (00:00 - 24:00)
        sb.AppendLine("  <!-- TimelineRegion -->");
        sb.AppendLine("  <g id=\"timelineRegion\">");
        for (int h = 0; h <= 24; h += 2)
        {
            double x = timelineLeft + (h / 24.0) * timelineWidth;
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <line x1=\"{x:F1}\" y1=\"{timelineTop + 15}\" x2=\"{x:F1}\" y2=\"{gridTop + 6 * queueRowHeight}\" stroke=\"{GridLineColor}\" stroke-width=\"1\" stroke-dasharray=\"2,2\"/>"));
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{x:F1}\" y=\"{timelineTop + 10}\" font-family=\"Arial, sans-serif\" font-size=\"11\" text-anchor=\"middle\" fill=\"#CCCCCC\">{h:D2}:00</text>"));
        }
        sb.AppendLine("  </g>");

        // 6 Queue Regions & 12 Subqueue Regions
        sb.AppendLine("  <!-- QueueRegions -->");
        for (int qIdx = 0; qIdx < inputPackage.Queues.Count; qIdx++)
        {
            var queue = inputPackage.Queues[qIdx];
            int qY = gridTop + qIdx * queueRowHeight;

            sb.AppendLine($"  <g id=\"queue_{EscapeXml(queue.QueueId)}\">");
            sb.AppendLine($"    <text x=\"30\" y=\"{qY + 40}\" font-family=\"Arial, sans-serif\" font-size=\"15\" font-weight=\"bold\" fill=\"{TextColor}\">{EscapeXml(queue.QueueId)}</text>");

            for (int sqIdx = 0; sqIdx < queue.Subqueues.Count; sqIdx++)
            {
                var subqueue = queue.Subqueues[sqIdx];
                int sqY = qY + sqIdx * 34;

                sb.AppendLine($"    <!-- Subqueue {EscapeXml(subqueue.SubqueueId)} -->");
                sb.AppendLine($"    <text x=\"80\" y=\"{sqY + 22}\" font-family=\"Arial, sans-serif\" font-size=\"12\" fill=\"#DDDDDD\">{EscapeXml(subqueue.SubqueueId)}</text>");
                // Background track for subqueue
                sb.AppendLine($"    <rect x=\"{timelineLeft}\" y=\"{sqY + 6}\" width=\"{timelineWidth}\" height=\"24\" fill=\"#2A2A2A\" rx=\"3\"/>");

                // Outage Intervals
                foreach (var interval in subqueue.Intervals)
                {
                    var (start, end) = interval.ParseTimes();
                    double startFrac = start.TotalHours / 24.0;
                    double endFrac = end.TotalHours / 24.0;
                    double x = timelineLeft + startFrac * timelineWidth;
                    double w = (endFrac - startFrac) * timelineWidth;

                    string fill = interval.Status.Equals("Possible", StringComparison.OrdinalIgnoreCase) ? PossibleColor : OutageColor;
                    string statusTitle = EscapeXml($"{interval.StartTime}–{interval.EndTime} ({interval.Status})");

                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <rect x=\"{x:F1}\" y=\"{sqY + 6}\" width=\"{w:F1}\" height=\"24\" fill=\"{fill}\" rx=\"2\"><title>{statusTitle}</title></rect>"));
                }
            }
            sb.AppendLine("  </g>");
        }

        // Legend / Footer Region
        int footerY = canvasHeight - 25;
        sb.AppendLine("  <!-- FooterRegion -->");
        sb.AppendLine("  <g id=\"footerRegion\">");
        sb.AppendLine($"    <rect x=\"{timelineLeft}\" y=\"{footerY - 14}\" width=\"16\" height=\"16\" fill=\"{OutageColor}\" rx=\"2\"/>");
        sb.AppendLine($"    <text x=\"{timelineLeft + 24}\" y=\"{footerY - 2}\" font-family=\"Arial, sans-serif\" font-size=\"12\" fill=\"{TextColor}\">Вимкнення</text>");

        sb.AppendLine($"    <rect x=\"{timelineLeft + 140}\" y=\"{footerY - 14}\" width=\"16\" height=\"16\" fill=\"{PossibleColor}\" rx=\"2\"/>");
        sb.AppendLine($"    <text x=\"{timelineLeft + 164}\" y=\"{footerY - 2}\" font-family=\"Arial, sans-serif\" font-size=\"12\" fill=\"{TextColor}\">Можливе вимкнення</text>");

        sb.AppendLine($"    <text x=\"{canvasWidth - 30}\" y=\"{footerY - 2}\" font-family=\"Arial, sans-serif\" font-size=\"11\" text-anchor=\"end\" fill=\"#888888\">SvitloSk Autonomous Publishing System</text>");
        sb.AppendLine("  </g>");

        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void ValidatePackage(GraphicInputPackage package)
    {
        if (package.Metadata == null)
            throw new ArgumentException("GraphicInputPackage Metadata cannot be null.");
        if (string.IsNullOrWhiteSpace(package.Metadata.PackageId))
            throw new ArgumentException("PackageId cannot be empty.");
        if (string.IsNullOrWhiteSpace(package.Metadata.TargetDate))
            throw new ArgumentException("TargetDate cannot be empty.");
        if (string.IsNullOrWhiteSpace(package.TerritorialScope))
            throw new ArgumentException("TerritorialScope cannot be empty.");
        if (package.Queues == null || package.Queues.Count != 6)
            throw new ArgumentException($"GraphicInputPackage must contain exactly 6 Queues, but found {package.Queues?.Count ?? 0}.");

        var seenSubqueues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var q in package.Queues)
        {
            if (string.IsNullOrWhiteSpace(q.QueueId))
                throw new ArgumentException("QueueId cannot be empty.");
            if (q.Subqueues == null || q.Subqueues.Count != 2)
                throw new ArgumentException($"Queue '{q.QueueId}' must contain exactly 2 Subqueues, but found {q.Subqueues?.Count ?? 0}.");

            foreach (var sq in q.Subqueues)
            {
                if (string.IsNullOrWhiteSpace(sq.SubqueueId))
                    throw new ArgumentException("SubqueueId cannot be empty.");
                if (!seenSubqueues.Add(sq.SubqueueId))
                    throw new ArgumentException($"Duplicate SubqueueId detected: '{sq.SubqueueId}'.");

                if (sq.Intervals != null)
                {
                    foreach (var interval in sq.Intervals)
                    {
                        if (interval == null)
                            throw new ArgumentException($"Null interval found in subqueue '{sq.SubqueueId}'.");
                        interval.ParseTimes(); // validates start/end formatting and bounds
                    }
                }
            }
        }
    }

    private static string EscapeXml(string? unescaped)
    {
        if (string.IsNullOrEmpty(unescaped)) return string.Empty;
        return unescaped
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}


