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
    // Canonical corporate colors
    public const string OutageColor = "#EE7221"; // Primary Orange / Restricted
    public const string PossibleColor = "#708090"; // Slate Gray / Possible restriction
    public const string BackgroundColor = "#1F2937"; // Dark Charcoal / Canvas Background
    public const string TrackColor = "#374151"; // Subqueue row background track
    public const string TextColor = "#F9FAFB"; // Crisp White
    public const string MutedTextColor = "#9CA3AF"; // Secondary Gray
    public const string GridLineColor = "#374151"; // Subtle Timeline Grid

    // SvitloSk Official Bulb Vector Path (scaled to 36x36 at position x,y)
    private const string BulbBaseVector = "M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z";
    private const string BulbGlassVector = "M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z";

    public byte[] AssembleSvg(GraphicInputPackage inputPackage)
    {
        if (inputPackage == null)
            throw new ArgumentNullException(nameof(inputPackage));

        ValidatePackage(inputPackage);

        // SVG Layout Geometry (Deterministic Canvas: 1200 x 780)
        int canvasWidth = 1200;
        int canvasHeight = 780;

        int headerHeight = 90;
        int timelineHeaderHeight = 35;
        int timelineLeft = 110;
        int timelineWidth = 1040;
        int timelineTop = headerHeight + 10;
        
        int gridTop = timelineTop + timelineHeaderHeight;
        int subqueueRowHeight = 40; // 12 subqueues = 480px total height

        // Flatten all 12 subqueues
        var allSubqueues = new List<SubqueueSchedule>();
        foreach (var q in inputPackage.Queues)
        {
            allSubqueues.AddRange(q.Subqueues);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{BackgroundColor}\"/>");

        // 1. Header Region with Official Bulb Logo and Publisher Timestamp
        sb.AppendLine("  <!-- HeaderRegion -->");
        sb.AppendLine("  <g id=\"headerRegion\">");
        // Logo Bulb Icon (Scaled from 512x512 to 44x44 at x=35, y=26)
        sb.AppendLine("    <g transform=\"translate(35, 26) scale(0.086)\">");
        sb.AppendLine($"      <path d=\"{BulbBaseVector}\" fill=\"{OutageColor}\"/>");
        sb.AppendLine($"      <path d=\"{BulbGlassVector}\" fill=\"{OutageColor}\"/>");
        sb.AppendLine("    </g>");

        sb.AppendLine($"    <text x=\"90\" y=\"46\" font-family=\"Arial, sans-serif\" font-size=\"22\" font-weight=\"bold\" fill=\"{TextColor}\">SVITLOSK | ГРАФІК ЗНЕСТРУМЛЕНЬ</text>");
        sb.AppendLine($"    <text x=\"90\" y=\"68\" font-family=\"Arial, sans-serif\" font-size=\"14\" fill=\"{MutedTextColor}\">Старокостянтинівська територіальна громада | 12 підчерг | Дата: {EscapeXml(inputPackage.Metadata.TargetDate)}</text>");

        sb.AppendLine($"    <text x=\"{canvasWidth - 40}\" y=\"44\" font-family=\"Arial, sans-serif\" font-size=\"13\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{OutageColor}\">Паблішер SvitloSk</text>");
        sb.AppendLine($"    <text x=\"{canvasWidth - 40}\" y=\"66\" font-family=\"Arial, sans-serif\" font-size=\"12\" text-anchor=\"end\" fill=\"{MutedTextColor}\">Створено: {EscapeXml(inputPackage.Metadata.GenerationTimestamp)}</text>");
        sb.AppendLine("  </g>");

        // 2. Timeline Axis Region (00:00 - 24:00)
        sb.AppendLine("  <!-- TimelineRegion -->");
        sb.AppendLine("  <g id=\"timelineRegion\">");
        for (int h = 0; h <= 24; h += 2)
        {
            double x = timelineLeft + (h / 24.0) * timelineWidth;
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <line x1=\"{x:F1}\" y1=\"{timelineTop + 15}\" x2=\"{x:F1}\" y2=\"{gridTop + 12 * subqueueRowHeight}\" stroke=\"{GridLineColor}\" stroke-width=\"1\" stroke-dasharray=\"2,2\"/>"));
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{x:F1}\" y=\"{timelineTop + 10}\" font-family=\"Arial, sans-serif\" font-size=\"11\" text-anchor=\"middle\" fill=\"{MutedTextColor}\">{h:D2}:00</text>"));
        }
        sb.AppendLine("  </g>");

        // 3. 12 Subqueues (Clean 1.1 - 6.2 list without group duplicates)
        sb.AppendLine("  <!-- SubqueueRegions -->");
        for (int sqIdx = 0; sqIdx < allSubqueues.Count; sqIdx++)
        {
            var subqueue = allSubqueues[sqIdx];
            int sqY = gridTop + sqIdx * subqueueRowHeight;

            // Extract short label e.g. "Черга 1.1" -> "1.1"
            string shortLabel = subqueue.SubqueueId.Replace("Черга ", "").Trim();

            sb.AppendLine($"  <g id=\"subqueue_{EscapeXml(shortLabel)}\">");
            sb.AppendLine($"    <text x=\"55\" y=\"{sqY + 22}\" font-family=\"Arial, sans-serif\" font-size=\"14\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"{TextColor}\">{EscapeXml(shortLabel)}</text>");
            
            // Background track for subqueue
            sb.AppendLine($"    <rect x=\"{timelineLeft}\" y=\"{sqY + 4}\" width=\"{timelineWidth}\" height=\"26\" fill=\"{TrackColor}\" rx=\"4\"/>");

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

                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <rect x=\"{x:F1}\" y=\"{sqY + 4}\" width=\"{w:F1}\" height=\"26\" fill=\"{fill}\" rx=\"3\"><title>{statusTitle}</title></rect>"));
            }
            sb.AppendLine("  </g>");
        }

        // 4. Legend & PWA QR Code Footer Region
        int footerY = canvasHeight - 90;
        sb.AppendLine("  <!-- FooterRegion -->");
        sb.AppendLine("  <g id=\"footerRegion\">");
        
        // Color Legend
        sb.AppendLine($"    <rect x=\"{timelineLeft}\" y=\"{footerY + 10}\" width=\"18\" height=\"18\" fill=\"{OutageColor}\" rx=\"3\"/>");
        sb.AppendLine($"    <text x=\"{timelineLeft + 28}\" y=\"{footerY + 24}\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"{TextColor}\">Вимкнення електроенергії (ГПВ)</text>");

        sb.AppendLine($"    <rect x=\"{timelineLeft + 270}\" y=\"{footerY + 10}\" width=\"18\" height=\"18\" fill=\"{PossibleColor}\" rx=\"3\"/>");
        sb.AppendLine($"    <text x=\"{timelineLeft + 298}\" y=\"{footerY + 24}\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"{TextColor}\">Можливе вимкнення</text>");

        sb.AppendLine($"    <text x=\"{timelineLeft}\" y=\"{footerY + 54}\" font-family=\"Arial, sans-serif\" font-size=\"12\" fill=\"{MutedTextColor}\">SvitloSk Autonomous Publishing System | Старокостянтинівська МТГ</text>");

        // PWA QR Code Vector Mock Block (Clean QR Pattern)
        int qrX = canvasWidth - 280;
        int qrY = footerY;
        sb.AppendLine($"    <g id=\"pwa_qr_code\" transform=\"translate({qrX}, {qrY})\">");
        sb.AppendLine("      <rect width=\"64\" height=\"64\" fill=\"#FFFFFF\" rx=\"4\"/>");
        // QR Position Patterns
        sb.AppendLine("      <rect x=\"6\" y=\"6\" width=\"18\" height=\"18\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"9\" y=\"9\" width=\"12\" height=\"12\" fill=\"#FFFFFF\"/>");
        sb.AppendLine("      <rect x=\"11\" y=\"11\" width=\"8\" height=\"8\" fill=\"#1F2937\"/>");

        sb.AppendLine("      <rect x=\"40\" y=\"6\" width=\"18\" height=\"18\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"43\" y=\"9\" width=\"12\" height=\"12\" fill=\"#FFFFFF\"/>");
        sb.AppendLine("      <rect x=\"45\" y=\"11\" width=\"8\" height=\"8\" fill=\"#1F2937\"/>");

        sb.AppendLine("      <rect x=\"6\" y=\"40\" width=\"18\" height=\"18\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"9\" y=\"43\" width=\"12\" height=\"12\" fill=\"#FFFFFF\"/>");
        sb.AppendLine("      <rect x=\"11\" y=\"45\" width=\"8\" height=\"8\" fill=\"#1F2937\"/>");

        // QR Data bits representation
        sb.AppendLine("      <rect x=\"28\" y=\"10\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"28\" y=\"22\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"16\" y=\"28\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"28\" y=\"34\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"40\" y=\"28\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"40\" y=\"40\" width=\"6\" height=\"6\" fill=\"#1F2937\"/>");
        sb.AppendLine("      <rect x=\"48\" y=\"48\" width=\"8\" height=\"8\" fill=\"#1F2937\"/>");

        // QR Description
        sb.AppendLine($"      <text x=\"76\" y=\"24\" font-family=\"Arial, sans-serif\" font-size=\"13\" font-weight=\"bold\" fill=\"{TextColor}\">Додаток SvitloSk PWA</text>");
        sb.AppendLine($"      <text x=\"76\" y=\"44\" font-family=\"Arial, sans-serif\" font-size=\"11\" fill=\"{MutedTextColor}\">Відскануйте QR-код для</text>");
        sb.AppendLine($"      <text x=\"76\" y=\"58\" font-family=\"Arial, sans-serif\" font-size=\"11\" fill=\"{MutedTextColor}\">онлайн-моніторингу черги</text>");
        sb.AppendLine("    </g>");

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


