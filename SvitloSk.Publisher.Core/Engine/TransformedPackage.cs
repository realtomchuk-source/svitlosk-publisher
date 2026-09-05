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
    // Canonical corporate colors (Light Theme aligned with PWA App)
    public const string BackgroundColor = "#F3F4F6"; // Canvas Light Gray Background
    public const string TrackColor = "#FFFFFF"; // Subqueue row background track (White)
    public const string TrackBorderColor = "#E5E7EB"; // Subqueue border outline
    public const string PoweredColor = "#EE7221"; // Primary Orange / Powered (Світло є)
    public const string OutageColor = "#374151"; // Dark Slate Gray / Outage (Знеструмлення)
    public const string PossibleColor = "#9CA3AF"; // Medium Gray / Possible Outage (Можливе)
    public const string PrimaryTextColor = "#111827"; // Dark Charcoal Text
    public const string MutedTextColor = "#6B7280"; // Medium Muted Gray
    public const string GridLineColor = "#E5E7EB"; // Timeline subtle grid line

    // SvitloSk Official Rounded Master Icon Paths
    private const string BulbMasterPath = "M222.289 345.677C221.196 436.829 58.655 424.572 73.6932 333.434H222.289V345.677ZM147.5 0.0861511C274.44 -4.33261 343.954 162.31 254.839 249.867C242.121 263.282 230.872 278.848 225.248 296.039H157.975V224.113C195.197 217.281 209.435 182.846 205.62 147.572C218.285 147.519 218.298 128.229 205.62 128.19H186.242V99.1174C186.242 93.7543 181.916 89.4271 176.554 89.427C171.191 89.427 166.864 93.7542 166.864 99.1174V128.19H128.121V99.1174C128.121 93.7543 123.795 89.427 118.432 89.427C113.07 89.427 108.743 93.7542 108.743 99.1174V128.19H89.3651C76.7002 128.242 76.6871 147.519 89.3651 147.572C85.563 182.846 99.7624 217.255 137.011 224.113V296.039H69.7381C64.1922 278.861 52.8645 263.282 40.16 249.88C-48.942 162.31 20.5331 -4.30678 147.5 0.0861511Z";

    private static string GetUkrainianDayOfWeek(DateTime date)
    {
        return date.DayOfWeek switch
        {
            DayOfWeek.Monday => "ПОНЕДІЛОК",
            DayOfWeek.Tuesday => "ВІВТОРОК",
            DayOfWeek.Wednesday => "СЕРЕДА",
            DayOfWeek.Thursday => "ЧЕТВЕР",
            DayOfWeek.Friday => "П'ЯТНИЦЯ",
            DayOfWeek.Saturday => "СУБОТА",
            DayOfWeek.Sunday => "НЕДІЛЯ",
            _ => ""
        };
    }

    public byte[] AssembleSvg(GraphicInputPackage inputPackage)
    {
        if (inputPackage == null)
            throw new ArgumentNullException(nameof(inputPackage));

        ValidatePackage(inputPackage);

        // 1:1 Square Canvas Geometry: 1080 x 1080 px
        int canvasWidth = 1080;
        int canvasHeight = 1080;

        int timelineLeft = 100;
        int timelineWidth = 940;
        int timelineTop = 135;
        int timelineHeaderHeight = 35;
        
        int gridTop = timelineTop + timelineHeaderHeight;
        int trackHeight = 36;
        int intraQueueGap = 8;  // Gap between 1.1 and 1.2
        int interQueueGap = 24; // Gap between 1.2 and 2.1 (3x larger)

        // Flatten all 12 subqueues
        var allSubqueues = new List<SubqueueSchedule>();
        foreach (var q in inputPackage.Queues)
        {
            allSubqueues.AddRange(q.Subqueues);
        }

        // Calculate Y positions for all 12 subqueues
        var subqueueYPositions = new int[allSubqueues.Count];
        int currentY = gridTop;
        for (int i = 0; i < allSubqueues.Count; i++)
        {
            subqueueYPositions[i] = currentY;
            if (i % 2 == 0)
            {
                currentY += trackHeight + intraQueueGap;
            }
            else
            {
                currentY += trackHeight + interQueueGap;
            }
        }
        int gridBottom = (subqueueYPositions.Length > 0 ? subqueueYPositions[^1] : gridTop) + trackHeight;

        // Parse target date and day of week
        string formattedDate = inputPackage.Metadata.TargetDate;
        string dayOfWeekStr = "НЕДІЛЯ";
        if (DateTime.TryParse(inputPackage.Metadata.TargetDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }

        // Format generation timestamp DD.MM.YYYY, HH:mm
        string formattedTimestamp = inputPackage.Metadata.GenerationTimestamp;
        if (DateTime.TryParse(inputPackage.Metadata.GenerationTimestamp, out var parsedGenTime))
        {
            formattedTimestamp = parsedGenTime.ToString("dd.MM.yyyy, HH:mm");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{BackgroundColor}\"/>");

        // 1. Header Region: Left Logo & SvitloSk Text | Right Day/Date & Scope Subtitle
        sb.AppendLine("  <!-- HeaderRegion -->");
        sb.AppendLine("  <g id=\"headerRegion\">");
        
        // App Master Icon (Dark Rounded Card with Orange Bulb & Plug)
        sb.AppendLine("    <g transform=\"translate(40, 30)\">");
        sb.AppendLine($"      <rect width=\"64\" height=\"64\" rx=\"16\" fill=\"{OutageColor}\"/>");
        // Orange Bulb & Base
        sb.AppendLine($"      <g transform=\"translate(10, 9) scale(0.086)\">");
        sb.AppendLine($"        <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"{PoweredColor}\"/>");
        sb.AppendLine($"        <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"{PoweredColor}\"/>");
        sb.AppendLine("      </g>");
        // Two-color text logo to the right of the icon, vertically centered
        sb.AppendLine($"      <text x=\"80\" y=\"46\" font-family=\"Arial, sans-serif\" font-size=\"36\" font-weight=\"bold\"><tspan fill=\"{PoweredColor}\">Svitlo</tspan><tspan fill=\"{OutageColor}\">Sk</tspan></text>");
        sb.AppendLine("    </g>");

        // Right Top Header: Day of Week & Date on line 1, Scope on line 2
        sb.AppendLine($"    <text x=\"1040\" y=\"60\" font-family=\"Arial, sans-serif\" font-size=\"26\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{PrimaryTextColor}\">{dayOfWeekStr}, {EscapeXml(formattedDate)}</text>");
        sb.AppendLine($"    <text x=\"1040\" y=\"86\" font-family=\"Arial, sans-serif\" font-size=\"16\" font-weight=\"500\" text-anchor=\"end\" fill=\"{MutedTextColor}\">Графік знеструмлень • Старокостянтинівська громада</text>");
        sb.AppendLine("  </g>");

        // 2. Timeline Axis Region (00:00 - 24:00)
        sb.AppendLine("  <!-- TimelineRegion -->");
        sb.AppendLine("  <g id=\"timelineRegion\">");
        for (int h = 0; h <= 24; h += 2)
        {
            double x = timelineLeft + (h / 24.0) * timelineWidth;
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <line x1=\"{x:F1}\" y1=\"{timelineTop + 18}\" x2=\"{x:F1}\" y2=\"{gridBottom + 6}\" stroke=\"{GridLineColor}\" stroke-width=\"1.5\" stroke-dasharray=\"3,3\"/>"));
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{x:F1}\" y=\"{timelineTop + 14}\" font-family=\"Arial, sans-serif\" font-size=\"13\" font-weight=\"600\" text-anchor=\"middle\" fill=\"{MutedTextColor}\">{h:D2}:00</text>"));
        }
        sb.AppendLine("  </g>");

        // 3. 12 Subqueues (Paired rows with distinct inter-queue spacing)
        sb.AppendLine("  <!-- SubqueueRegions -->");
        for (int sqIdx = 0; sqIdx < allSubqueues.Count; sqIdx++)
        {
            var subqueue = allSubqueues[sqIdx];
            int sqY = subqueueYPositions[sqIdx];

            // Extract short label e.g. "Черга 1.1" -> "1.1"
            string shortLabel = subqueue.SubqueueId.Replace("Черга ", "").Trim();

            sb.AppendLine($"  <g id=\"subqueue_{EscapeXml(shortLabel)}\">");
            sb.AppendLine($"    <text x=\"50\" y=\"{sqY + 24}\" font-family=\"Arial, sans-serif\" font-size=\"17\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"{PrimaryTextColor}\">{EscapeXml(shortLabel)}</text>");
            
            // Background track for subqueue (Powered Orange fill as base)
            sb.AppendLine($"    <rect x=\"{timelineLeft}\" y=\"{sqY}\" width=\"{timelineWidth}\" height=\"{trackHeight}\" fill=\"{PoweredColor}\" rx=\"6\" stroke=\"{TrackBorderColor}\" stroke-width=\"1\"/>");

            // Outage Intervals (Dark Slate Gray for Outages / Light Gray for Possible)
            foreach (var interval in subqueue.Intervals)
            {
                var (start, end) = interval.ParseTimes();
                double startFrac = start.TotalHours / 24.0;
                double endFrac = end.TotalHours / 24.0;
                double x = timelineLeft + startFrac * timelineWidth;
                double w = (endFrac - startFrac) * timelineWidth;

                string fill = interval.Status.Equals("Possible", StringComparison.OrdinalIgnoreCase) ? PossibleColor : OutageColor;
                string statusTitle = EscapeXml($"{interval.StartTime}–{interval.EndTime} ({interval.Status})");

                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <rect x=\"{x:F1}\" y=\"{sqY}\" width=\"{w:F1}\" height=\"{trackHeight}\" fill=\"{fill}\" rx=\"5\"><title>{statusTitle}</title></rect>"));
            }
            sb.AppendLine("  </g>");
        }

        // 4. Footer Region: Left System String + Right QR Code Block
        int footerY = canvasHeight - 110;
        sb.AppendLine("  <!-- FooterRegion -->");
        sb.AppendLine("  <g id=\"footerRegion\">");
        
        // Exact Left Footer String
        sb.AppendLine($"    <text x=\"40\" y=\"{footerY + 50}\" font-family=\"Arial, sans-serif\" font-size=\"14\" fill=\"{MutedTextColor}\">SvitloSk Autonomous Publishing System | {EscapeXml(formattedTimestamp)}</text>");

        // Right QR Code Block (QR at far right, text to the left of it)
        int qrSize = 76;
        int qrX = canvasWidth - 40 - qrSize;
        int qrY = footerY + 12;

        sb.AppendLine($"    <!-- PWA QR Block -->");
        // Two-line explanatory text to the left of QR code
        sb.AppendLine($"    <text x=\"{qrX - 16}\" y=\"{qrY + 32}\" font-family=\"Arial, sans-serif\" font-size=\"14\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{PrimaryTextColor}\">Відскануй для</text>");
        sb.AppendLine($"    <text x=\"{qrX - 16}\" y=\"{qrY + 52}\" font-family=\"Arial, sans-serif\" font-size=\"14\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{PrimaryTextColor}\">моніторингу знеструмлень</text>");

        // QR Code Container Box
        sb.AppendLine($"    <g id=\"pwa_qr_code\" transform=\"translate({qrX}, {qrY})\">");
        sb.AppendLine($"      <rect width=\"{qrSize}\" height=\"{qrSize}\" fill=\"#FFFFFF\" rx=\"10\" stroke=\"{TrackBorderColor}\" stroke-width=\"1.5\"/>");
        
        // QR Position Marker Top-Left
        sb.AppendLine($"      <rect x=\"8\" y=\"8\" width=\"20\" height=\"20\" fill=\"{OutageColor}\" rx=\"3\"/>");
        sb.AppendLine("      <rect x=\"11\" y=\"11\" width=\"14\" height=\"14\" fill=\"#FFFFFF\" rx=\"2\"/>");
        sb.AppendLine($"      <rect x=\"14\" y=\"14\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");

        // QR Position Marker Top-Right
        sb.AppendLine($"      <rect x=\"48\" y=\"8\" width=\"20\" height=\"20\" fill=\"{OutageColor}\" rx=\"3\"/>");
        sb.AppendLine("      <rect x=\"51\" y=\"11\" width=\"14\" height=\"14\" fill=\"#FFFFFF\" rx=\"2\"/>");
        sb.AppendLine($"      <rect x=\"54\" y=\"14\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");

        // QR Position Marker Bottom-Left
        sb.AppendLine($"      <rect x=\"8\" y=\"48\" width=\"20\" height=\"20\" fill=\"{OutageColor}\" rx=\"3\"/>");
        sb.AppendLine("      <rect x=\"11\" y=\"51\" width=\"14\" height=\"14\" fill=\"#FFFFFF\" rx=\"2\"/>");
        sb.AppendLine($"      <rect x=\"14\" y=\"54\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");

        // Stylized Data Pattern bits
        sb.AppendLine($"      <rect x=\"33\" y=\"12\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"33\" y=\"26\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"18\" y=\"33\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"33\" y=\"40\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"48\" y=\"33\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"48\" y=\"48\" width=\"8\" height=\"8\" fill=\"{OutageColor}\" rx=\"1\"/>");
        sb.AppendLine($"      <rect x=\"58\" y=\"58\" width=\"10\" height=\"10\" fill=\"{OutageColor}\" rx=\"1\"/>");
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


