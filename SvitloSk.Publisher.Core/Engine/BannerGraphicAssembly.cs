using System;

namespace SvitloSk.Publisher.Core.Engine;

public interface IBannerGraphicAssembly
{
    byte[] AssembleDayHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleEmergencyHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleTomorrowHeaderSvg(string tomorrowDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleNoOutagesSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");

    byte[] AssembleFacebookDayHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleFacebookEmergencyHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleFacebookTomorrowHeaderSvg(string tomorrowDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
    byte[] AssembleFacebookNoOutagesSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада");
}

public class BannerGraphicAssembly : IBannerGraphicAssembly
{
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

    private static double MeasureArialTextWidth(string text, double fontSize)
    {
        double width = 0;
        foreach (char c in text)
        {
            if (c == ' ' || c == '.' || c == ',' || c == '•') width += fontSize * 0.28;
            else if (c == 'і' || c == 'ї' || c == 'I' || c == 'І' || c == '1' || c == 'l' || c == 'i' || c == '!' || c == '\'') width += fontSize * 0.28;
            else if (c == 'j' || c == 'r' || c == 't' || c == 'f' || c == 'г' || c == 'с' || c == 'ь') width += fontSize * 0.38;
            else if (c == 'м' || c == 'ж' || c == 'ш' || c == 'щ' || c == 'ю' || c == 'w' || c == 'm') width += fontSize * 0.72;
            else if (c == 'М' || c == 'Ш' || c == 'Щ' || c == 'Ю' || c == 'W' || c == 'M' || c == 'Ж') width += fontSize * 0.85;
            else if (char.IsUpper(c)) width += fontSize * 0.68;
            else width += fontSize * 0.54;
        }
        return width;
    }

    private static double MeasureArial28pxWidth(string text)
    {
        double width = 0;
        foreach (char c in text)
        {
            if (c == ' ' || c == '.' || c == '•') width += 10.0;
            else if (c == 'І' || c == 'I' || c == '1' || c == 'l') width += 11.0;
            else if (c == 'М' || c == 'Ш' || c == 'Щ' || c == 'Ю' || c == 'W' || c == 'M' || c == 'Ж') width += 25.0;
            else width += 20.0;
        }
        return width;
    }

    public byte[] AssembleDayHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1080;
        int canvasHeight = 480;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(editionDate, @"(\d{4}-\d{2}-\d{2})|(\d{2}\.\d{2}\.\d{4})");
            if (match.Success && DateTime.TryParse(match.Value, out var regexDate))
            {
                dayOfWeekStr = GetUkrainianDayOfWeek(regexDate);
                formattedDate = regexDate.ToString("dd.MM.yyyy");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Right-Side Background Aesthetic Circular Arcs (from Reference Design)
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"260\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"40\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"200\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"170\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"6\" stroke-dasharray=\"350 400\" stroke-linecap=\"round\" transform=\"rotate(-45 960 240)\"/>");

        // Right-Side Stylized Bulb Accent
        sb.AppendLine("  <!-- Outlined Orange Bulb matching reference -->");
        sb.AppendLine("  <g transform=\"translate(850, 115) scale(0.48)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section (Left-Aligned per Reference)
        sb.AppendLine($"  <text x=\"75\" y=\"115\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЖУРНАЛ</text>");
        sb.AppendLine($"  <text x=\"75\" y=\"195\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕНЬ</text>");

        // Date Line with Orange Accent Vertical Pill Bar (Unified 50px font)
        sb.AppendLine($"  <rect x=\"75\" y=\"226\" width=\"14\" height=\"58\" rx=\"7\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"108\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");
        
        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.85 + 24;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{108 + dayOffset:F1}\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Bottom Territory Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 34px white text
        double textWidth = MeasureArialTextWidth(territorialScope, 34) * 1.12;
        double paddingX = 24.0;
        double pillWidth = textWidth + 2 * paddingX;
        double pillX = (canvasWidth - pillWidth) / 2.0;
        double centerX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{pillX:F1}\" y=\"350\" width=\"{pillWidth:F1}\" height=\"66\" rx=\"18\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{centerX:F1}\" y=\"394\" font-family=\"Arial, sans-serif\" font-size=\"34\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"479\" x2=\"{canvasWidth}\" y2=\"479\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleEmergencyHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1080;
        int canvasHeight = 480;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(editionDate, @"(\d{4}-\d{2}-\d{2})|(\d{2}\.\d{2}\.\d{4})");
            if (match.Success && DateTime.TryParse(match.Value, out var regexDate))
            {
                dayOfWeekStr = GetUkrainianDayOfWeek(regexDate);
                formattedDate = regexDate.ToString("dd.MM.yyyy");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Emergency Alert Decorative Arcs (Red / Amber tones)
        sb.AppendLine("  <!-- Emergency Alert Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"260\" fill=\"none\" stroke=\"#FEE2E2\" stroke-width=\"40\" opacity=\"0.8\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"200\" fill=\"#FFF1F2\" stroke=\"#FECACA\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"170\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"6\" stroke-dasharray=\"350 400\" stroke-linecap=\"round\" transform=\"rotate(-45 960 240)\"/>");

        // Emergency Alert Bulb Accent
        sb.AppendLine("  <!-- Outlined Emergency Red Bulb -->");
        sb.AppendLine("  <g transform=\"translate(850, 115) scale(0.48)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section: АВАРІЙНІ ЗНЕСТРУМЛЕННЯ
        sb.AppendLine($"  <text x=\"75\" y=\"115\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#DC2626\">АВАРІЙНІ</text>");
        sb.AppendLine($"  <text x=\"75\" y=\"195\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕННЯ</text>");

        // Date Line with Red Accent Vertical Pill Bar
        sb.AppendLine($"  <rect x=\"75\" y=\"226\" width=\"14\" height=\"58\" rx=\"7\" fill=\"#EF4444\"/>");
        sb.AppendLine($"  <text x=\"108\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"#DC2626\">{EscapeXml(dayOfWeekStr)}</text>");
        
        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.85 + 24;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{108 + dayOffset:F1}\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Bottom Territory Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 34px white text
        double textWidth = MeasureArialTextWidth(territorialScope, 34) * 1.12;
        double paddingX = 24.0;
        double pillWidth = textWidth + 2 * paddingX;
        double pillX = (canvasWidth - pillWidth) / 2.0;
        double centerX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{pillX:F1}\" y=\"350\" width=\"{pillWidth:F1}\" height=\"66\" rx=\"18\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{centerX:F1}\" y=\"394\" font-family=\"Arial, sans-serif\" font-size=\"34\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"479\" x2=\"{canvasWidth}\" y2=\"479\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleTomorrowHeaderSvg(string tomorrowDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(tomorrowDate))
            throw new ArgumentException("Tomorrow date cannot be null or empty.", nameof(tomorrowDate));

        int canvasWidth = 1080;
        int canvasHeight = 480;

        string formattedDate = tomorrowDate;
        string dayOfWeekStr = "ЗАВТРА";
        if (DateTime.TryParse(tomorrowDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(tomorrowDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Right-Side Background Aesthetic Circular Arcs (Tomorrow Distinct Palette: Soft Slate)
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"260\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"40\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"200\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"170\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"6\" stroke-dasharray=\"350 400\" stroke-linecap=\"round\" transform=\"rotate(-45 960 240)\"/>");

        // Right-Side Stylized Bulb Accent (Soft Slate #94A3B8 for Tomorrow Forecast)
        sb.AppendLine("  <!-- Outlined Soft Slate Gray Bulb matching forecast identity -->");
        sb.AppendLine("  <g transform=\"translate(850, 115) scale(0.48)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section (Left-Aligned per Reference)
        // 2-Line High-Impact Bold Title (ПРОГНОЗ / НА ЗАВТРА)
        sb.AppendLine($"  <text x=\"75\" y=\"115\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ПРОГНОЗ</text>");
        sb.AppendLine($"  <text x=\"75\" y=\"195\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">НА ЗАВТРА</text>");

        // Date Line with Orange Accent Vertical Pill Bar (Restored orange per user design request)
        sb.AppendLine($"  <rect x=\"75\" y=\"226\" width=\"14\" height=\"58\" rx=\"7\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"108\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");
        
        double tomDayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.85 + 24;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{108 + tomDayOffset:F1}\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Bottom Territory Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 34px white text
        double tomTextWidth = MeasureArialTextWidth(territorialScope, 34) * 1.12;
        double tomPaddingX = 24.0;
        double tomPillWidth = tomTextWidth + 2 * tomPaddingX;
        double tomPillX = (canvasWidth - tomPillWidth) / 2.0;
        double tomCenterX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{tomPillX:F1}\" y=\"350\" width=\"{tomPillWidth:F1}\" height=\"66\" rx=\"18\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{tomCenterX:F1}\" y=\"394\" font-family=\"Arial, sans-serif\" font-size=\"34\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"479\" x2=\"{canvasWidth}\" y2=\"479\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleNoOutagesSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1080;
        int canvasHeight = 600;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(editionDate, @"(\d{4}-\d{2}-\d{2})|(\d{2}\.\d{2}\.\d{4})");
            if (match.Success && DateTime.TryParse(match.Value, out var regexDate))
            {
                dayOfWeekStr = GetUkrainianDayOfWeek(regexDate);
                formattedDate = regexDate.ToString("dd.MM.yyyy");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Right-Side Background Aesthetic Circular Arcs (matching standard header design)
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"260\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"40\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"200\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"960\" cy=\"240\" r=\"170\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"6\" stroke-dasharray=\"350 400\" stroke-linecap=\"round\" transform=\"rotate(-45 960 240)\"/>");

        // Right-Side Stylized Bulb Accent
        sb.AppendLine("  <!-- Outlined Orange Bulb matching reference -->");
        sb.AppendLine("  <g transform=\"translate(850, 115) scale(0.48)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section (Left-Aligned per Reference)
        sb.AppendLine($"  <text x=\"75\" y=\"115\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЖУРНАЛ</text>");
        sb.AppendLine($"  <text x=\"75\" y=\"195\" font-family=\"Arial, sans-serif\" font-size=\"72\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕНЬ</text>");

        // Date Line with Orange Accent Vertical Pill Bar (Unified 50px font)
        sb.AppendLine($"  <rect x=\"75\" y=\"226\" width=\"14\" height=\"58\" rx=\"7\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"108\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");
        
        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.85 + 24;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{108 + dayOffset:F1}\" y=\"274\" font-family=\"Arial, sans-serif\" font-size=\"50\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Bottom Territory Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 34px white text
        double textWidth = MeasureArialTextWidth(territorialScope, 34) * 1.12;
        double paddingX = 24.0;
        double pillWidth = textWidth + 2 * paddingX;
        double pillX = (canvasWidth - pillWidth) / 2.0;
        double centerX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{pillX:F1}\" y=\"340\" width=\"{pillWidth:F1}\" height=\"66\" rx=\"18\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{centerX:F1}\" y=\"384\" font-family=\"Arial, sans-serif\" font-size=\"34\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom Status Banner: ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ
        sb.AppendLine("  <!-- Bottom Summary Banner (Green, single centered line) -->");
        sb.AppendLine($"  <rect x=\"75\" y=\"440\" width=\"930\" height=\"105\" rx=\"24\" fill=\"#ECFDF5\" stroke=\"#A7F3D0\" stroke-width=\"2.5\"/>");
        sb.AppendLine($"  <circle cx=\"130\" cy=\"492\" r=\"22\" fill=\"#10B981\"/>");
        sb.AppendLine("  <path d=\"M121 492 L127 498 L139 484\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"4.0\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        sb.AppendLine("  <text x=\"175\" y=\"505\" font-family=\"Arial, sans-serif\" font-size=\"36\" font-weight=\"900\" fill=\"#065F46\">ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ</text>");

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"599\" x2=\"{canvasWidth}\" y2=\"599\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleFacebookDayHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1200;
        int canvasHeight = 630;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(editionDate, @"(\d{4}-\d{2}-\d{2})|(\d{2}\.\d{2}\.\d{4})");
            if (match.Success && DateTime.TryParse(match.Value, out var regexDate))
            {
                dayOfWeekStr = GetUkrainianDayOfWeek(regexDate);
                formattedDate = regexDate.ToString("dd.MM.yyyy");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Facebook 1.91:1 Landscape Background Circles
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"320\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"48\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"250\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"210\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"7\" stroke-dasharray=\"400 480\" stroke-linecap=\"round\" transform=\"rotate(-45 1050 315)\"/>");

        // Bulb Accent
        sb.AppendLine("  <!-- Outlined Orange Bulb matching reference -->");
        sb.AppendLine("  <g transform=\"translate(915, 160) scale(0.60)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section
        sb.AppendLine($"  <text x=\"85\" y=\"140\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЖУРНАЛ</text>");
        sb.AppendLine($"  <text x=\"85\" y=\"230\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕНЬ</text>");

        // Date Line with Orange Accent Vertical Pill Bar
        sb.AppendLine($"  <rect x=\"85\" y=\"270\" width=\"16\" height=\"66\" rx=\"8\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"120\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");

        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.95 + 28;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{120 + dayOffset:F1}\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 38px white text
        double fbTextWidth = MeasureArialTextWidth(territorialScope, 38) * 1.12;
        double fbPaddingX = 28.0;
        double fbPillWidth = fbTextWidth + 2 * fbPaddingX;
        double fbPillX = (canvasWidth - fbPillWidth) / 2.0;
        double fbCenterX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{fbPillX:F1}\" y=\"418\" width=\"{fbPillWidth:F1}\" height=\"72\" rx=\"20\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{fbCenterX:F1}\" y=\"466\" font-family=\"Arial, sans-serif\" font-size=\"38\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"629\" x2=\"{canvasWidth}\" y2=\"629\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleFacebookEmergencyHeaderSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1200;
        int canvasHeight = 630;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(editionDate, @"(\d{4}-\d{2}-\d{2})|(\d{2}\.\d{2}\.\d{4})");
            if (match.Success && DateTime.TryParse(match.Value, out var regexDate))
            {
                dayOfWeekStr = GetUkrainianDayOfWeek(regexDate);
                formattedDate = regexDate.ToString("dd.MM.yyyy");
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Emergency Alert Decorative Arcs (Red / Amber tones)
        sb.AppendLine("  <!-- Background Emergency Alert Arcs -->");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"320\" fill=\"none\" stroke=\"#FEE2E2\" stroke-width=\"48\" opacity=\"0.8\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"250\" fill=\"#FFF1F2\" stroke=\"#FECACA\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"210\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"7\" stroke-dasharray=\"400 480\" stroke-linecap=\"round\" transform=\"rotate(-45 1050 315)\"/>");

        // Emergency Alert Bulb Accent
        sb.AppendLine("  <!-- Outlined Emergency Red Bulb -->");
        sb.AppendLine("  <g transform=\"translate(915, 160) scale(0.60)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"#EF4444\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section: АВАРІЙНІ ЗНЕСТРУМЛЕННЯ
        sb.AppendLine($"  <text x=\"85\" y=\"140\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#DC2626\">АВАРІЙНІ</text>");
        sb.AppendLine($"  <text x=\"85\" y=\"230\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕННЯ</text>");

        // Date Line with Red Accent Vertical Pill Bar
        sb.AppendLine($"  <rect x=\"85\" y=\"270\" width=\"16\" height=\"66\" rx=\"8\" fill=\"#EF4444\"/>");
        sb.AppendLine($"  <text x=\"120\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"#DC2626\">{EscapeXml(dayOfWeekStr)}</text>");

        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.95 + 28;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{120 + dayOffset:F1}\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 38px white text
        double fbTextWidth = MeasureArialTextWidth(territorialScope, 38) * 1.12;
        double fbPaddingX = 28.0;
        double fbPillWidth = fbTextWidth + 2 * fbPaddingX;
        double fbPillX = (canvasWidth - fbPillWidth) / 2.0;
        double fbCenterX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{fbPillX:F1}\" y=\"418\" width=\"{fbPillWidth:F1}\" height=\"72\" rx=\"20\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{fbCenterX:F1}\" y=\"466\" font-family=\"Arial, sans-serif\" font-size=\"38\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"629\" x2=\"{canvasWidth}\" y2=\"629\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleFacebookTomorrowHeaderSvg(string tomorrowDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(tomorrowDate))
            throw new ArgumentException("Tomorrow date cannot be null or empty.", nameof(tomorrowDate));

        int canvasWidth = 1200;
        int canvasHeight = 630;

        string formattedDate = tomorrowDate;
        string dayOfWeekStr = "ЗАВТРА";
        if (DateTime.TryParse(tomorrowDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(tomorrowDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Background Decorative Arcs (Tomorrow Soft Slate Palette)
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"320\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"48\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"250\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"210\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"7\" stroke-dasharray=\"400 480\" stroke-linecap=\"round\" transform=\"rotate(-45 1050 315)\"/>");

        // Bulb Accent
        sb.AppendLine("  <!-- Outlined Soft Slate Gray Bulb matching forecast identity -->");
        sb.AppendLine("  <g transform=\"translate(915, 160) scale(0.60)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"#94A3B8\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section
        sb.AppendLine($"  <text x=\"85\" y=\"140\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ПРОГНОЗ</text>");
        sb.AppendLine($"  <text x=\"85\" y=\"230\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">НА ЗАВТРА</text>");

        // Date Line with Orange Accent Vertical Pill Bar
        sb.AppendLine($"  <rect x=\"85\" y=\"270\" width=\"16\" height=\"66\" rx=\"8\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"120\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");

        double tomDayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.95 + 28;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{120 + tomDayOffset:F1}\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 38px white text
        double tomFbTextWidth = MeasureArialTextWidth(territorialScope, 38) * 1.12;
        double tomFbPaddingX = 28.0;
        double tomFbPillWidth = tomFbTextWidth + 2 * tomFbPaddingX;
        double tomFbPillX = (canvasWidth - tomFbPillWidth) / 2.0;
        double tomFbCenterX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{tomFbPillX:F1}\" y=\"418\" width=\"{tomFbPillWidth:F1}\" height=\"72\" rx=\"20\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{tomFbCenterX:F1}\" y=\"466\" font-family=\"Arial, sans-serif\" font-size=\"38\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"629\" x2=\"{canvasWidth}\" y2=\"629\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] AssembleFacebookNoOutagesSvg(string editionDate, string territorialScope = "Старокостянтинівська міська територіальна громада")
    {
        if (string.IsNullOrWhiteSpace(editionDate))
            throw new ArgumentException("Edition date cannot be null or empty.", nameof(editionDate));

        int canvasWidth = 1200;
        int canvasHeight = 630;

        string formattedDate = editionDate;
        string dayOfWeekStr = "СЬОГОДНІ";
        if (DateTime.TryParse(editionDate, out var parsedDate))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedDate);
            formattedDate = parsedDate.ToString("dd.MM.yyyy");
        }
        else if (DateTime.TryParseExact(editionDate, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedExact))
        {
            dayOfWeekStr = GetUkrainianDayOfWeek(parsedExact);
            formattedDate = parsedExact.ToString("dd.MM.yyyy");
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {canvasWidth} {canvasHeight}\" width=\"{canvasWidth}\" height=\"{canvasHeight}\">");
        sb.AppendLine($"  <rect width=\"100%\" height=\"100%\" fill=\"{GraphicAssembly.BackgroundColor}\"/>");

        // Background Decorative Arcs
        sb.AppendLine("  <!-- Background Decorative Arcs -->");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"320\" fill=\"none\" stroke=\"#E2E8F0\" stroke-width=\"48\" opacity=\"0.6\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"250\" fill=\"#F8FAFC\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine($"  <circle cx=\"1050\" cy=\"315\" r=\"210\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"7\" stroke-dasharray=\"400 480\" stroke-linecap=\"round\" transform=\"rotate(-45 1050 315)\"/>");

        // Bulb Accent
        sb.AppendLine("  <!-- Outlined Orange Bulb matching reference -->");
        sb.AppendLine("  <g transform=\"translate(915, 160) scale(0.60)\">");
        sb.AppendLine($"    <path d=\"M336 409.33C334.83 508.55 159.82 495.2 176 396H336V409.33Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine($"    <path d=\"M256 36C118.69 31.25 43.56 211.41 139.92 306.09C153.66 320.59 165.91 337.42 171.91 356H244.66V278.23C204.38 270.82 189.03 233.61 193.14 195.47C179.44 195.42 179.44 174.57 193.14 174.52H214.09V143.09C214.09 137.3 218.77 132.61 224.57 132.61C230.37 132.61 235.05 137.29 235.05 143.09V174.52H276.95V143.09C276.95 137.3 281.63 132.61 287.43 132.61C293.23 132.61 297.91 137.29 297.91 143.09V174.52H318.86C332.56 174.57 332.56 195.42 318.86 195.47C322.98 233.61 307.59 270.84 267.34 278.23V356H340.09C346.17 337.42 358.34 320.58 372.09 306.08C468.46 211.41 393.29 31.22 256.01 36H256Z\" fill=\"none\" stroke=\"{GraphicAssembly.PoweredColor}\" stroke-width=\"16\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        sb.AppendLine("  </g>");

        // Typography Section
        sb.AppendLine($"  <text x=\"85\" y=\"140\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЖУРНАЛ</text>");
        sb.AppendLine($"  <text x=\"85\" y=\"230\" font-family=\"Arial, sans-serif\" font-size=\"80\" font-weight=\"900\" letter-spacing=\"1.5\" fill=\"#0F2942\">ЗНЕСТРУМЛЕНЬ</text>");

        // Date Line with Orange Accent Vertical Pill Bar
        sb.AppendLine($"  <rect x=\"85\" y=\"270\" width=\"16\" height=\"66\" rx=\"8\" fill=\"{GraphicAssembly.PoweredColor}\"/>");
        sb.AppendLine($"  <text x=\"120\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"{GraphicAssembly.PoweredColor}\">{EscapeXml(dayOfWeekStr)}</text>");

        double dayOffset = MeasureArial28pxWidth(dayOfWeekStr) * 1.95 + 28;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  <text x=\"{120 + dayOffset:F1}\" y=\"324\" font-family=\"Arial, sans-serif\" font-size=\"54\" font-weight=\"900\" fill=\"#0F2942\">{EscapeXml(formattedDate)}</text>"));

        // Scope Pill Badge: Dark Navy #0F2942 centered relative to banner with larger 38px white text
        double fbTextWidth = MeasureArialTextWidth(territorialScope, 38) * 1.12;
        double fbPaddingX = 28.0;
        double fbPillWidth = fbTextWidth + 2 * fbPaddingX;
        double fbPillX = (canvasWidth - fbPillWidth) / 2.0;
        double fbCenterX = canvasWidth / 2.0;
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <rect x=\"{fbPillX:F1}\" y=\"410\" width=\"{fbPillWidth:F1}\" height=\"72\" rx=\"20\" fill=\"#0F2942\"/>"));
        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"  <text x=\"{fbCenterX:F1}\" y=\"458\" font-family=\"Arial, sans-serif\" font-size=\"38\" font-weight=\"800\" text-anchor=\"middle\" fill=\"#FFFFFF\">{EscapeXml(territorialScope)}</text>"));

        // Bottom Summary Banner (Green, single centered line)
        sb.AppendLine("  <!-- Bottom Summary Banner (Green) -->");
        sb.AppendLine($"  <rect x=\"85\" y=\"500\" width=\"1030\" height=\"95\" rx=\"24\" fill=\"#ECFDF5\" stroke=\"#A7F3D0\" stroke-width=\"2.5\"/>");
        sb.AppendLine($"  <circle cx=\"145\" cy=\"547\" r=\"22\" fill=\"#10B981\"/>");
        sb.AppendLine("  <path d=\"M136 547 L142 553 L154 539\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"4.0\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
        sb.AppendLine("  <text x=\"190\" y=\"559\" font-family=\"Arial, sans-serif\" font-size=\"34\" font-weight=\"900\" fill=\"#065F46\">ЕЛЕКТРОПОСТАЧАННЯ СТАБІЛЬНЕ</text>");

        // Bottom subtle border divider
        sb.AppendLine($"  <line x1=\"0\" y1=\"629\" x2=\"{canvasWidth}\" y2=\"629\" stroke=\"{GraphicAssembly.TrackBorderColor}\" stroke-width=\"2\"/>");
        sb.AppendLine("</svg>");

        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
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
