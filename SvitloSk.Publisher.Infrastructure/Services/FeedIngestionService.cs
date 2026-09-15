using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;

namespace SvitloSk.Publisher.Infrastructure.Services;

/// <summary>
/// Service responsible for fetching, parsing, and assembling canonical EditorialInput packages
/// from remote feeds (today.txt, tomorrow.txt, graphic schedules) and local fallbacks.
/// </summary>
public class FeedIngestionService
{
    private readonly IOutageFeedParser _parser;
    private readonly IBannerGraphicAssembly _bannerAssembly;
    private readonly IGraphicRasterizer _rasterizer;
    private readonly EditorialContentTransformer _transformer;

    public FeedIngestionService(
        IOutageFeedParser parser,
        IBannerGraphicAssembly bannerAssembly,
        IGraphicRasterizer rasterizer,
        EditorialContentTransformer transformer)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _bannerAssembly = bannerAssembly ?? throw new ArgumentNullException(nameof(bannerAssembly));
        _rasterizer = rasterizer ?? throw new ArgumentNullException(nameof(rasterizer));
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    public async Task<EditorialInput> IngestEditorialInputAsync(
        HttpClient httpClient,
        bool isDryRun,
        CancellationToken cancellationToken = default)
    {
        string todayFeedContent = "============================================\nДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 20.08.2026\n============================================\nВідключень не зафіксовано.\n============================================\nКІНЕЦЬ ДОКУМЕНТУ\n";
        string? tomorrowFeedContent = null;
        bool tomorrowAvailable = false;

        string localFeedFallbackPath = @"../ParserAktualVidkl/starokostiantyniv-outages/data/tg_posts/today.txt";

        if (!isDryRun)
        {
            try
            {
                Console.WriteLine("[INFO] Fetching online today.txt feed from GitHub...");
                todayFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt", cancellationToken).ConfigureAwait(false);
                Console.WriteLine("[INFO] Successfully fetched today.txt online.");
            }
            catch (Exception onlineEx)
            {
                Console.WriteLine($"[WARN] Could not fetch today.txt online ({onlineEx.Message}). Falling back to local today.txt.");
                if (File.Exists(localFeedFallbackPath))
                {
                    todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                Console.WriteLine("[INFO] Fetching online tomorrow.txt forecast from GitHub...");
                tomorrowFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/tomorrow.txt", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(tomorrowFeedContent) && !tomorrowFeedContent.Contains("404"))
                {
                    tomorrowAvailable = true;
                    Console.WriteLine("[INFO] Successfully fetched tomorrow.txt online.");
                }
            }
            catch
            {
                Console.WriteLine("[INFO] Tomorrow forecast not found or unavailable online.");
            }
        }
        else
        {
            if (File.Exists(localFeedFallbackPath))
            {
                todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
            }
        }

        // Parse date from feed metadata
        string editionDate;
        var dateMatch = Regex.Match(todayFeedContent, @"Дата:\s*(\d{2})\.(\d{2})\.(\d{4})");
        if (dateMatch.Success)
        {
            editionDate = $"{dateMatch.Groups[3].Value}-{dateMatch.Groups[2].Value}-{dateMatch.Groups[1].Value}";
            Console.WriteLine($"[INFO] Parsed EditionDate from feed: {editionDate}");
        }
        else
        {
            throw new InvalidOperationException("[FATAL] Malformed or missing Date in feed metadata. Fail-Closed.");
        }

        var packages = new List<InputTerritoryPackage>
        {
            new InputTerritoryPackage("Громада", todayFeedContent, null, true)
        };

        // Format Tomorrow forecast if available
        DateTime parsedToday = DateTime.Parse(editionDate);
        DateTime tomorrowDate = parsedToday.AddDays(1);
        string tomorrowLabel = tomorrowDate.ToString("yyyy-MM-dd");

        if (tomorrowAvailable && !string.IsNullOrWhiteSpace(tomorrowFeedContent))
        {
            var tomorrowTerritoryPackages = new List<InputTerritoryPackage>();

            if (tomorrowFeedContent.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ") || tomorrowFeedContent.Contains("ЗНЕСТРУМЛЕННЯ"))
            {
                var tomRecords = _parser.Parse(tomorrowFeedContent);
                var tomAggregated = TerritoryAggregator.AggregateByTerritory(tomRecords);
                if (tomAggregated.Count > 0)
                {
                    foreach (var tAgg in tomAggregated)
                    {
                        string formattedTom = _transformer.RenderAggregatedTerritoryPost(tAgg, isTomorrow: true, tomorrowDate: tomorrowLabel);
                        tomorrowTerritoryPackages.Add(new InputTerritoryPackage($"tomorrow_{tAgg.TerritoryId}", formattedTom, null, false));
                    }
                }
            }
            else if (!tomorrowFeedContent.Contains("не заплановано") && !tomorrowFeedContent.Contains("не зафіксовано"))
            {
                string details = _transformer.RenderRecordDetails(tomorrowFeedContent.Trim());
                if (!string.IsNullOrWhiteSpace(details))
                {
                    string formattedTomorrow = $"<b>Старокостянтинівська міська територіальна громада</b>\n\nОчікується обмеження електропостачання.\n\nОрієнтовний графік відключень:\n{details}";
                    tomorrowTerritoryPackages.Add(new InputTerritoryPackage("tomorrow", formattedTomorrow, null, false));
                }
            }

            // Add Tomorrow Separator Banner ONLY if there are actual tomorrow forecast posts to display
            if (tomorrowTerritoryPackages.Count > 0)
            {
                byte[]? tomBannerPng = null;
                try
                {
                    byte[] tomBannerSvg = _bannerAssembly.AssembleTomorrowHeaderSvg(tomorrowLabel);
                    tomBannerPng = _rasterizer.RasterizeSvgToPng(tomBannerSvg, 1080, 480);
                }
                catch (Exception tomEx)
                {
                    Console.WriteLine($"[WARN] Could not render tomorrow separator banner: {tomEx.Message}");
                }

                if (tomBannerPng != null && tomBannerPng.Length > 0)
                {
                    packages.Add(new InputTerritoryPackage("tomorrow_separator", "", tomBannerPng, false));
                }

                packages.AddRange(tomorrowTerritoryPackages);
            }
        }

        // Ingestion of 12-subqueue graphic schedule from SvitloSk parser repository or local fixture
        GraphicInputPackage? graphicPackage = null;
        string? graphicJsonContent = null;

        if (!isDryRun)
        {
            // Priority 1: Check if tomorrow's graphic schedule JSON is published
            string onlineGraphicTomorrowUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{tomorrowLabel}.json";
            try
            {
                Console.WriteLine($"[INFO] Fetching graphic schedule JSON for tomorrow from {onlineGraphicTomorrowUrl}...");
                graphicJsonContent = await httpClient.GetStringAsync(onlineGraphicTomorrowUrl, cancellationToken).ConfigureAwait(false);
                Console.WriteLine($"[INFO] Successfully fetched tomorrow's graphic schedule JSON ({tomorrowLabel}).");
            }
            catch
            {
                // Priority 2: Fallback to today's graphic schedule JSON
                string onlineGraphicTodayUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{editionDate}.json";
                try
                {
                    Console.WriteLine($"[INFO] Tomorrow's graphic not found. Checking today's graphic schedule from {onlineGraphicTodayUrl}...");
                    graphicJsonContent = await httpClient.GetStringAsync(onlineGraphicTodayUrl, cancellationToken).ConfigureAwait(false);
                    Console.WriteLine($"[INFO] Successfully fetched today's graphic schedule JSON ({editionDate}).");
                }
                catch (Exception gEx)
                {
                    Console.WriteLine($"[INFO] Online graphic JSON for {tomorrowLabel} / {editionDate} is not published ({gEx.Message}). Graphic schedule skipped.");
                }
            }
        }
        else
        {
            string sampleFixture = "local/fixtures/sample_graphic_schedule.json";
            if (File.Exists(sampleFixture))
            {
                graphicJsonContent = await File.ReadAllTextAsync(sampleFixture, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(graphicJsonContent))
        {
            try
            {
                graphicPackage = _parser.ParseLegacyGraphicJson(graphicJsonContent, "Старокостянтинівська МТГ");
                Console.WriteLine("[INFO] Successfully parsed 12-subqueue graphic package.");
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"[WARN] Failed to parse graphic schedule JSON: {parseEx.Message}");
            }
        }

        bool hasActiveTomorrowPackages = packages.Any(p => p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase));

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: hasActiveTomorrowPackages,
            GraphicPackage: graphicPackage
        );
    }

    public async Task<EditorialInput> IngestFacebookEditorialInputAsync(
        HttpClient httpClient,
        bool isDryRun,
        CancellationToken cancellationToken = default)
    {
        string todayFeedContent = "============================================\nДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 20.08.2026\n============================================\nВідключень не зафіксовано.\n============================================\nКІНЕЦЬ ДОКУМЕНТУ\n";
        string? tomorrowFeedContent = null;
        bool tomorrowAvailable = false;

        string localFeedFallbackPath = @"../ParserAktualVidkl/starokostiantyniv-outages/data/tg_posts/today.txt";

        if (!isDryRun)
        {
            try
            {
                Console.WriteLine("[INFO][Facebook] Fetching online today.txt feed from GitHub...");
                todayFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt", cancellationToken).ConfigureAwait(false);
                Console.WriteLine("[INFO][Facebook] Successfully fetched today.txt online.");
            }
            catch (Exception onlineEx)
            {
                Console.WriteLine($"[WARN][Facebook] Could not fetch today.txt online ({onlineEx.Message}). Falling back to local today.txt.");
                if (File.Exists(localFeedFallbackPath))
                {
                    todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
                }
            }

            try
            {
                Console.WriteLine("[INFO][Facebook] Fetching online tomorrow.txt forecast from GitHub...");
                tomorrowFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/tomorrow.txt", cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(tomorrowFeedContent) && !tomorrowFeedContent.Contains("404"))
                {
                    tomorrowAvailable = true;
                    Console.WriteLine("[INFO][Facebook] Successfully fetched tomorrow.txt online.");
                }
            }
            catch
            {
                Console.WriteLine("[INFO][Facebook] Tomorrow forecast not found or unavailable online.");
            }
        }
        else
        {
            if (File.Exists(localFeedFallbackPath))
            {
                todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
            }
        }

        // Parse date from feed metadata
        string editionDate;
        var dateMatch = Regex.Match(todayFeedContent, @"Дата:\s*(\d{2})\.(\d{2})\.(\d{4})");
        if (dateMatch.Success)
        {
            editionDate = $"{dateMatch.Groups[3].Value}-{dateMatch.Groups[2].Value}-{dateMatch.Groups[1].Value}";
            Console.WriteLine($"[INFO][Facebook] Parsed EditionDate from feed: {editionDate}");
        }
        else
        {
            throw new InvalidOperationException("[FATAL] Malformed or missing Date in feed metadata. Fail-Closed.");
        }

        var packages = new List<InputTerritoryPackage>();
        var todayRecords = _parser.Parse(todayFeedContent);
        var todayAggregated = TerritoryAggregator.AggregateByTerritory(todayRecords);

        // 1. Emergency post (Created ONLY if emergency records exist)
        bool hasEmergencies = todayAggregated.Any(t => t.EmergencyRecords != null && t.EmergencyRecords.Count > 0);
        if (hasEmergencies)
        {
            string? emergText = FacebookContentFormatter.FormatFacebookEmergencyPost(editionDate, todayAggregated, _transformer);
            if (!string.IsNullOrWhiteSpace(emergText))
            {
                byte[] emergSvg = _bannerAssembly.AssembleFacebookEmergencyHeaderSvg(editionDate);
                byte[] emergPng = _rasterizer.RasterizeSvgToPng(emergSvg, 1200, 630);
                packages.Add(new InputTerritoryPackage("fb_emergency", emergText, emergPng, false));
                Console.WriteLine("[INFO][Facebook] Assembled emergency package 'fb_emergency'.");
            }
        }
        else
        {
            Console.WriteLine("[INFO][Facebook] No active emergency outages recorded for today. 'fb_emergency' post omitted.");
        }

        // 2. Planned post (Always created/maintained for today)
        string plannedText = FacebookContentFormatter.FormatFacebookPlannedPost(editionDate, todayAggregated, _transformer);
        bool hasPlanned = todayAggregated.Any(t => t.PlannedRecords != null && t.PlannedRecords.Count > 0);
        byte[] planSvg = hasPlanned
            ? _bannerAssembly.AssembleFacebookDayHeaderSvg(editionDate)
            : _bannerAssembly.AssembleFacebookNoOutagesSvg(editionDate);
        byte[] planPng = _rasterizer.RasterizeSvgToPng(planSvg, 1200, 630);
        packages.Add(new InputTerritoryPackage("fb_planned", plannedText, planPng, true));
        Console.WriteLine($"[INFO][Facebook] Assembled planned package 'fb_planned' (HasPlanned: {hasPlanned}).");

        // 3. Tomorrow forecast post (Created when tomorrow data exists)
        DateTime parsedToday = DateTime.Parse(editionDate);
        DateTime tomorrowDate = parsedToday.AddDays(1);
        string tomorrowLabel = tomorrowDate.ToString("yyyy-MM-dd");

        if (tomorrowAvailable && !string.IsNullOrWhiteSpace(tomorrowFeedContent))
        {
            if (tomorrowFeedContent.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ") || tomorrowFeedContent.Contains("ЗНЕСТРУМЛЕННЯ"))
            {
                var tomRecords = _parser.Parse(tomorrowFeedContent);
                var tomAggregated = TerritoryAggregator.AggregateByTerritory(tomRecords);
                string? tomText = FacebookContentFormatter.FormatFacebookTomorrowPost(tomorrowLabel, tomAggregated, _transformer);
                if (!string.IsNullOrWhiteSpace(tomText))
                {
                    byte[] tomSvg = _bannerAssembly.AssembleFacebookTomorrowHeaderSvg(tomorrowLabel);
                    byte[] tomPng = _rasterizer.RasterizeSvgToPng(tomSvg, 1200, 630);
                    packages.Add(new InputTerritoryPackage("fb_tomorrow", tomText, tomPng, false));
                    Console.WriteLine("[INFO][Facebook] Assembled tomorrow forecast package 'fb_tomorrow'.");
                }
            }
        }

        // 4. Ingestion of 12-subqueue graphic schedule (if available)
        GraphicInputPackage? graphicPackage = null;
        string? graphicJsonContent = null;

        if (!isDryRun)
        {
            string onlineGraphicTomorrowUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{tomorrowLabel}.json";
            try
            {
                graphicJsonContent = await httpClient.GetStringAsync(onlineGraphicTomorrowUrl, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                string onlineGraphicTodayUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{editionDate}.json";
                try
                {
                    graphicJsonContent = await httpClient.GetStringAsync(onlineGraphicTodayUrl, cancellationToken).ConfigureAwait(false);
                }
                catch { }
            }
        }
        else
        {
            string sampleFixture = "local/fixtures/sample_graphic_schedule.json";
            if (File.Exists(sampleFixture))
            {
                graphicJsonContent = await File.ReadAllTextAsync(sampleFixture, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!string.IsNullOrWhiteSpace(graphicJsonContent))
        {
            try
            {
                graphicPackage = _parser.ParseLegacyGraphicJson(graphicJsonContent, "Старокостянтинівська МТГ");
                Console.WriteLine("[INFO][Facebook] Successfully parsed 12-subqueue graphic package.");
            }
            catch (Exception parseEx)
            {
                Console.WriteLine($"[WARN][Facebook] Failed to parse graphic schedule JSON: {parseEx.Message}");
            }
        }

        bool hasTomorrow = packages.Any(p => p.TerritoryId == "fb_tomorrow");

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: hasTomorrow,
            GraphicPackage: graphicPackage
        );
    }
}
