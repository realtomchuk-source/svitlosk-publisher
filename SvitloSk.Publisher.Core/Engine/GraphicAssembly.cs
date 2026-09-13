using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Engine;

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

        // 1. Header Region: Left Logo & Large SvitloSk Text | Right Single-line Header with Orange Day
        sb.AppendLine("  <!-- HeaderRegion -->");
        sb.AppendLine("  <g id=\"headerRegion\">");
        
        // App Master Icon (Dark Rounded Card with Orange Bulb & Plug)
        sb.AppendLine("    <g transform=\"translate(40, 26)\">");
        sb.AppendLine($"      <rect width=\"64\" height=\"64\" rx=\"16\" fill=\"{OutageColor}\"/>");
        // Orange Bulb & Base
        sb.AppendLine($"      <g transform=\"translate(10, 9) scale(0.086)\">");
        sb.AppendLine($"        <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"{PoweredColor}\"/>");
        sb.AppendLine($"        <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"{PoweredColor}\"/>");
        sb.AppendLine("      </g>");
        // Two-color text logo to the right of the icon, exactly matching button height (52px bold/black)
        sb.AppendLine($"      <text x=\"80\" y=\"51\" font-family=\"Arial, sans-serif\" font-size=\"52\" font-weight=\"900\" letter-spacing=\"-0.5\"><tspan fill=\"{PoweredColor}\">Svitlo</tspan><tspan fill=\"{OutageColor}\">Sk</tspan></text>");
        sb.AppendLine("    </g>");

        // Right Top Header: Single line uppercase header with orange day of week
        double titleStartX = 386;
        double dayStartX = 740;
        double dateStartX = 896;

        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{titleStartX}\" y=\"58\" font-family=\"Arial, sans-serif\" font-size=\"28\" font-weight=\"bold\" fill=\"{PrimaryTextColor}\">ГРАФІК ЗНЕСТРУМЛЕНЬ</text>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{dayStartX}\" y=\"58\" font-family=\"Arial, sans-serif\" font-size=\"28\" font-weight=\"bold\" fill=\"{PoweredColor}\">{dayOfWeekStr}</text>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    <text x=\"{dateStartX}\" y=\"58\" font-family=\"Arial, sans-serif\" font-size=\"28\" font-weight=\"bold\" fill=\"{PrimaryTextColor}\">{EscapeXml(formattedDate)}</text>"));
        sb.AppendLine($"    <text x=\"1040\" y=\"88\" font-family=\"Arial, sans-serif\" font-size=\"16\" font-weight=\"500\" text-anchor=\"end\" fill=\"{MutedTextColor}\">Старокостянтинівська територіальна громада</text>");
        sb.AppendLine("  </g>");

        // 2. 6 Queue Blocks (Borderless layout with horizontal line dividers)
        int blockTop = 100;
        int blockHeight = 148;

        int badgeX = 58;
        int trackLeft = 90;
        int trackWidth = 950;
        int trackH = 32;

        sb.AppendLine("  <!-- QueueBlocksRegion -->");
        sb.AppendLine("  <g id=\"queueBlocksRegion\">");

        for (int qIdx = 0; qIdx < inputPackage.Queues.Count; qIdx++)
        {
            var queue = inputPackage.Queues[qIdx];
            int blockY = blockTop + qIdx * blockHeight;

            var sq1 = queue.Subqueues.Count > 0 ? queue.Subqueues[0] : null;
            var sq2 = queue.Subqueues.Count > 1 ? queue.Subqueues[1] : null;

            string shortLabel1 = sq1?.SubqueueId.Replace("Черга ", "").Trim() ?? $"{qIdx + 1}.1";
            string shortLabel2 = sq2?.SubqueueId.Replace("Черга ", "").Trim() ?? $"{qIdx + 1}.2";

            int sq1TrackY = blockY + 25;
            int timelineY = blockY + 74;
            int sq2TrackY = blockY + 91;

            sb.AppendLine($"    <g id=\"queue_block_{qIdx + 1}\">");

            // Left Queue Badges: 1.1 and 1.2
            sb.AppendLine($"      <text x=\"{badgeX}\" y=\"{sq1TrackY + 24}\" font-family=\"Arial, sans-serif\" font-size=\"24\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"{PrimaryTextColor}\">{EscapeXml(shortLabel1)}</text>");
            sb.AppendLine($"      <text x=\"{badgeX}\" y=\"{sq2TrackY + 24}\" font-family=\"Arial, sans-serif\" font-size=\"24\" font-weight=\"bold\" text-anchor=\"middle\" fill=\"{PrimaryTextColor}\">{EscapeXml(shortLabel2)}</text>");

            // Central Timeline Pill Track Background
            int timelineHeight = 24;
            int timelineTop = timelineY - timelineHeight / 2;
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <rect x=\"{trackLeft}\" y=\"{timelineTop}\" width=\"{trackWidth}\" height=\"{timelineHeight}\" rx=\"5\" fill=\"#FFFFFF\" stroke=\"#E2E8F0\" stroke-width=\"1\"/>"));

            // 48-Slot Dual-Sided Precision Ruler (Every 30 mins)
            for (int slot = 0; slot <= 48; slot++)
            {
                double hours = slot * 0.5;
                double x = trackLeft + (hours / 24.0) * trackWidth;

                if (slot % 2 == 0)
                {
                    int h = slot / 2;
                    if (h % 2 == 0)
                    {
                        string anchor = h == 0 ? "start" : (h == 24 ? "end" : "middle");
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <text x=\"{x:F1}\" y=\"{timelineY + 4.5}\" font-family=\"Arial, sans-serif\" font-size=\"13\" font-weight=\"bold\" text-anchor=\"{anchor}\" fill=\"#334155\">{h:D2}</text>"));
                        
                        if (h > 0 && h < 24)
                        {
                            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop}\" x2=\"{x:F1}\" y2=\"{timelineTop + 3.5}\" stroke=\"#94A3B8\" stroke-width=\"1.2\"/>"));
                            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop + timelineHeight - 3.5}\" x2=\"{x:F1}\" y2=\"{timelineTop + timelineHeight}\" stroke=\"#94A3B8\" stroke-width=\"1.2\"/>"));
                        }
                    }
                    else
                    {
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop}\" x2=\"{x:F1}\" y2=\"{timelineTop + 7.5}\" stroke=\"#64748B\" stroke-width=\"1.5\"/>"));
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop + timelineHeight - 7.5}\" x2=\"{x:F1}\" y2=\"{timelineTop + timelineHeight}\" stroke=\"#64748B\" stroke-width=\"1.5\"/>"));
                    }
                }
                else
                {
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop}\" x2=\"{x:F1}\" y2=\"{timelineTop + 4}\" stroke=\"#94A3B8\" stroke-width=\"1\" opacity=\"0.85\"/>"));
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"{x:F1}\" y1=\"{timelineTop + timelineHeight - 4}\" x2=\"{x:F1}\" y2=\"{timelineTop + timelineHeight}\" stroke=\"#94A3B8\" stroke-width=\"1\" opacity=\"0.85\"/>"));
                }
            }

            // Render Subqueue 1
            if (sq1 != null)
            {
                RenderSubqueueTrack(sb, sq1, shortLabel1, trackLeft, sq1TrackY, trackWidth, trackH, isTopTrack: true, markerY: blockY + 20);
            }

            // Render Subqueue 2
            if (sq2 != null)
            {
                RenderSubqueueTrack(sb, sq2, shortLabel2, trackLeft, sq2TrackY, trackWidth, trackH, isTopTrack: false, markerY: blockY + 138);
            }

            // Horizontal Divider between queues
            if (qIdx < inputPackage.Queues.Count - 1)
            {
                int dividerY = blockY + blockHeight;
                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"      <line x1=\"40\" y1=\"{dividerY}\" x2=\"1040\" y2=\"{dividerY}\" stroke=\"#E5E7EB\" stroke-width=\"1.5\"/>"));
            }

            sb.AppendLine("    </g>");
        }
        sb.AppendLine("  </g>");

        // 4. Footer Region: Left System String + Right QR Code Block
        int footerY = canvasHeight - 110;
        sb.AppendLine("  <!-- FooterRegion -->");
        sb.AppendLine("  <g id=\"footerRegion\">");
        
        // Exact Left Footer String
        sb.AppendLine($"    <text x=\"40\" y=\"{footerY + 50}\" font-family=\"Arial, sans-serif\" font-size=\"14\" fill=\"{MutedTextColor}\">SvitloSk Autonomous Publishing System | {EscapeXml(formattedTimestamp)}</text>");

        // Right QR Code Block
        int qrSize = 76;
        int qrX = canvasWidth - 40 - qrSize;
        int qrY = footerY + 12;

        sb.AppendLine($"    <!-- PWA QR Block -->");
        sb.AppendLine($"    <text x=\"{qrX - 16}\" y=\"{qrY + 32}\" font-family=\"Arial, sans-serif\" font-size=\"14\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{PrimaryTextColor}\">Відскануй для</text>");
        sb.AppendLine($"    <text x=\"{qrX - 16}\" y=\"{qrY + 52}\" font-family=\"Arial, sans-serif\" font-size=\"14\" font-weight=\"bold\" text-anchor=\"end\" fill=\"{PrimaryTextColor}\">моніторингу знеструмлень</text>");

        sb.AppendLine($"    <!-- Real PWA QR Code -->");
        sb.AppendLine($"    <g id=\"pwa_qr_code\" transform=\"translate({qrX}, {qrY})\">");
        sb.AppendLine($"      <rect width=\"{qrSize}\" height=\"{qrSize}\" fill=\"#FFFFFF\" rx=\"10\" stroke=\"{TrackBorderColor}\" stroke-width=\"1.5\"/>");
        sb.AppendLine($"      <image href=\"data:image/png;base64,{QrCodeAsset.PwaQrBase64Png}\" x=\"4\" y=\"4\" width=\"{qrSize - 8}\" height=\"{qrSize - 8}\" preserveAspectRatio=\"xMidYMid meet\"/>");
        sb.AppendLine("    </g>");

        sb.AppendLine("  </g>");

        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static void RenderSubqueueTrack(
        System.Text.StringBuilder sb,
        SubqueueSchedule subqueue,
        string shortLabel,
        int trackLeft,
        int trackY,
        int trackWidth,
        int trackHeight,
        bool isTopTrack,
        int markerY)
    {
        sb.AppendLine($"      <g id=\"subqueue_{EscapeXml(shortLabel)}\">");

        // Background track (Powered Orange base)
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <rect x=\"{trackLeft}\" y=\"{trackY}\" width=\"{trackWidth}\" height=\"{trackHeight}\" fill=\"{PoweredColor}\" rx=\"6\" stroke=\"{TrackBorderColor}\" stroke-width=\"1\"/>"));

        // Transition points collection for markers
        var transitionHours = new HashSet<string>();

        // Render Outage Intervals
        if (subqueue.Intervals != null)
        {
            foreach (var interval in subqueue.Intervals)
            {
                var (start, end) = interval.ParseTimes();
                double startFrac = start.TotalHours / 24.0;
                double endFrac = end.TotalHours / 24.0;
                double x = trackLeft + startFrac * trackWidth;
                double w = (endFrac - startFrac) * trackWidth;

                string fill = interval.Status.Equals("Possible", StringComparison.OrdinalIgnoreCase) ? PossibleColor : OutageColor;
                string statusTitle = EscapeXml($"{interval.StartTime}–{interval.EndTime} ({interval.Status})");

                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <rect x=\"{x:F1}\" y=\"{trackY}\" width=\"{w:F1}\" height=\"{trackHeight}\" fill=\"{fill}\" rx=\"5\"><title>{statusTitle}</title></rect>"));

                if (!string.IsNullOrEmpty(interval.StartTime) && interval.StartTime != "00:00" && interval.StartTime != "0:00")
                {
                    transitionHours.Add(interval.StartTime);
                }
                if (!string.IsNullOrEmpty(interval.EndTime) && interval.EndTime != "24:00" && interval.EndTime != "00:00" && interval.EndTime != "0:00")
                {
                    transitionHours.Add(interval.EndTime);
                }
            }
        }

        // 1-Hour segment vertical dividing micro-lines inside track (24 square blocks)
        for (int h = 1; h < 24; h++)
        {
            double divX = trackLeft + (h / 24.0) * trackWidth;
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <line x1=\"{divX:F1}\" y1=\"{trackY}\" x2=\"{divX:F1}\" y2=\"{trackY + trackHeight}\" stroke=\"#FFFFFF\" stroke-width=\"1.5\" opacity=\"0.5\"/>"));
        }

        // Render Dynamic Transition Time Markers
        foreach (var timeStr in transitionHours)
        {
            if (TimeSpan.TryParse(timeStr, out var ts))
            {
                double frac = ts.TotalHours / 24.0;
                double markX = trackLeft + frac * trackWidth;
                string anchor = ts.TotalHours <= 1.0 ? "start" : (ts.TotalHours >= 23.0 ? "end" : "middle");

                int tickY1 = isTopTrack ? trackY : trackY + trackHeight;
                int tickY2 = isTopTrack ? trackY - 3 : trackY + trackHeight + 3;
                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <line x1=\"{markX:F1}\" y1=\"{tickY1}\" x2=\"{markX:F1}\" y2=\"{tickY2}\" stroke=\"{OutageColor}\" stroke-width=\"1.5\"/>"));
                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <circle cx=\"{markX:F1}\" cy=\"{tickY2}\" r=\"1.5\" fill=\"{OutageColor}\"/>"));

                sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"        <text x=\"{markX:F1}\" y=\"{markerY}\" font-family=\"Arial, sans-serif\" font-size=\"13\" font-weight=\"bold\" text-anchor=\"{anchor}\" fill=\"{PrimaryTextColor}\">{EscapeXml(timeStr)}</text>"));
            }
        }

        sb.AppendLine("      </g>");
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
                        interval.ParseTimes();
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
