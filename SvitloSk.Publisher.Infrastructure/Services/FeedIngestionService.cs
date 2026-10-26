using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

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

    public Func<DateTime> UtcNowProvider { get; set; } = () => DateTime.UtcNow;

    public async Task<EditorialInput> IngestEditorialInputAsync(
        HttpClient httpClient,
        bool isDryRun,
        CancellationToken cancellationToken = default)
    {
        string todayFeedContent = "============================================\nДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 20.08.2026\n============================================\nВідключень не зафіксовано.\n============================================\nКІНЕЦЬ ДОКУМЕНТУ\n";
        string? tomorrowFeedContent = null;
        bool tomorrowAvailable = false;
        DateTime sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());

        string localFeedFallbackPath = @"../ParserAktualVidkl/starokostiantyniv-outages/data/tg_posts/today.txt";

        if (!isDryRun)
        {
            try
            {
                Console.WriteLine("[INFO] Fetching online today.txt feed from GitHub...");
                todayFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt", cancellationToken).ConfigureAwait(false);
                sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());
                Console.WriteLine($"[INFO] Successfully fetched today.txt online at {sourceQueryTime:HH:mm}.");
            }
            catch (Exception onlineEx)
            {
                Console.WriteLine($"[WARN] Could not fetch today.txt online ({onlineEx.Message}). Falling back to local today.txt.");
                if (File.Exists(localFeedFallbackPath))
                {
                    todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
                    sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());
                }
            }

            bool isTomorrowTimeGateOpen = isDryRun || KyivClock.ToKyiv(UtcNowProvider()).Hour >= 12;

            if (isTomorrowTimeGateOpen)
            {
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
                Console.WriteLine("[INFO] Tomorrow forecast time gate is closed (< 12:00 Kyiv time). Skipping online tomorrow.txt fetch.");
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

            IReadOnlyList<OutageRecord>? tomRecords = null;

            if (tomorrowFeedContent.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ") || tomorrowFeedContent.Contains("ЗНЕСТРУМЛЕННЯ"))
            {
                tomRecords = _parser.Parse(tomorrowFeedContent);
                var tomAggregated = TerritoryAggregator.AggregateByTerritory(tomRecords);
                if (tomAggregated.Count > 0)
                {
                    var sortedTomAggregated = tomAggregated
                        .OrderBy(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) || t.CanonicalName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    foreach (var tAgg in sortedTomAggregated)
                    {
                        string formattedTom = _transformer.RenderAggregatedTerritoryPost(tAgg, isTomorrow: true, tomorrowDate: tomorrowLabel);
                        tomorrowTerritoryPackages.Add(new InputTerritoryPackage($"tomorrow_{tAgg.TerritoryId}", formattedTom, null, false));
                    }
                }
            }
            else if (!tomorrowFeedContent.Contains("не заплановано", StringComparison.OrdinalIgnoreCase) &&
                     !tomorrowFeedContent.Contains("не зафіксовано", StringComparison.OrdinalIgnoreCase))
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

                string tomCaption = "";
                if (tomRecords != null && tomRecords.Count > 0)
                {
                    var tomStats = TerritoryAggregator.CalculateSummaryStats(tomRecords);
                    tomCaption = _transformer.RenderJournalHeader(tomorrowLabel, tomStats);
                }

                if (tomBannerPng != null && tomBannerPng.Length > 0)
                {
                    packages.Add(new InputTerritoryPackage("tomorrow_separator", tomCaption, tomBannerPng, false));
                }
                else if (!string.IsNullOrWhiteSpace(tomCaption))
                {
                    packages.Add(new InputTerritoryPackage("tomorrow_separator", tomCaption, null, false));
                }

                packages.AddRange(tomorrowTerritoryPackages);
            }
        }

        // Ingestion of 12-subqueue graphic schedule from SvitloSk parser repository or local fixture
        // Only publish if at least one of the 12 subqueues has scheduled outages (HasAnyOutages == true).
        GraphicInputPackage? graphicPackage = await FetchActiveGraphicScheduleAsync(httpClient, tomorrowLabel, editionDate, isDryRun, logPrefix: "", cancellationToken).ConfigureAwait(false);

        bool hasActiveTomorrowPackages = packages.Any(p => p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase));

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: hasActiveTomorrowPackages,
            GraphicPackage: graphicPackage,
            SourceQueryTime: sourceQueryTime
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
        var rawTodayRecords = _parser.Parse(todayFeedContent);
        var todayRecords = await GetCumulativeTodayRecordsAsync(httpClient, editionDate, rawTodayRecords, isDryRun, cancellationToken).ConfigureAwait(false);
        var todayAggregated = TerritoryAggregator.AggregateByTerritory(todayRecords);

        // Daily journal post (Consolidates all planned and emergency outages for today in a single post)
        string plannedText = FacebookContentFormatter.FormatFacebookPlannedPost(editionDate, todayAggregated, _transformer);
        bool hasOutages = todayAggregated.Any(t =>
            (t.PlannedRecords != null && t.PlannedRecords.Count > 0) ||
            (t.EmergencyRecords != null && t.EmergencyRecords.Count > 0));
        byte[] planSvg = hasOutages
            ? _bannerAssembly.AssembleFacebookDayHeaderSvg(editionDate)
            : _bannerAssembly.AssembleFacebookNoOutagesSvg(editionDate);
        byte[] planPng = _rasterizer.RasterizeSvgToPng(planSvg, 1200, 630);
        packages.Add(new InputTerritoryPackage("fb_planned", plannedText, planPng, true));
        Console.WriteLine($"[INFO][Facebook] Assembled journal package 'fb_planned' (HasOutages: {hasOutages}).");

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

        // 4. Ingestion of 12-subqueue graphic schedule (only if it has outages across at least one subqueue)
        GraphicInputPackage? graphicPackage = await FetchActiveGraphicScheduleAsync(httpClient, tomorrowLabel, editionDate, isDryRun, logPrefix: "[Facebook]", cancellationToken).ConfigureAwait(false);

        bool hasTomorrow = packages.Any(p => p.TerritoryId == "fb_tomorrow");

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: hasTomorrow,
            GraphicPackage: graphicPackage
        );
    }

    public async Task<EditorialInput> IngestWhatsAppEditorialInputAsync(
        HttpClient httpClient,
        bool isDryRun,
        IReadOnlyList<string>? historicalTerritoryIds = null,
        CancellationToken cancellationToken = default)
    {
        string todayFeedContent = "============================================\nДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 20.08.2026\n============================================\nВідключень не зафіксовано.\n============================================\nКІНЕЦЬ ДОКУМЕНТУ\n";
        string? tomorrowFeedContent = null;
        bool tomorrowAvailable = false;
        DateTime sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());

        string localFeedFallbackPath = @"../ParserAktualVidkl/starokostiantyniv-outages/data/tg_posts/today.txt";

        if (!isDryRun)
        {
            try
            {
                Console.WriteLine("[INFO][WhatsApp] Fetching online today.txt feed from GitHub...");
                todayFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/today.txt", cancellationToken).ConfigureAwait(false);
                sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());
                Console.WriteLine($"[INFO][WhatsApp] Successfully fetched today.txt online at {sourceQueryTime:HH:mm}.");
            }
            catch (Exception onlineEx)
            {
                Console.WriteLine($"[WARN][WhatsApp] Could not fetch today.txt online ({onlineEx.Message}). Falling back to local today.txt.");
                if (File.Exists(localFeedFallbackPath))
                {
                    todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
                    sourceQueryTime = KyivClock.ToKyiv(UtcNowProvider());
                }
            }

            bool isTomorrowTimeGateOpen = isDryRun || KyivClock.ToKyiv(UtcNowProvider()).Hour >= 12;
            if (isTomorrowTimeGateOpen)
            {
                try
                {
                    Console.WriteLine("[INFO][WhatsApp] Fetching online tomorrow.txt forecast from GitHub...");
                    tomorrowFeedContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/main/data/tg_posts/tomorrow.txt", cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(tomorrowFeedContent) && !tomorrowFeedContent.Contains("404"))
                    {
                        tomorrowAvailable = true;
                        Console.WriteLine("[INFO][WhatsApp] Successfully fetched tomorrow.txt online.");
                    }
                }
                catch
                {
                    Console.WriteLine("[INFO][WhatsApp] Tomorrow forecast not found or unavailable online.");
                }
            }
            else
            {
                Console.WriteLine("[INFO][WhatsApp] Tomorrow forecast time gate is closed (< 12:00 Kyiv time). Skipping tomorrow.txt fetch.");
            }
        }
        else if (File.Exists(localFeedFallbackPath))
        {
            todayFeedContent = await File.ReadAllTextAsync(localFeedFallbackPath, cancellationToken).ConfigureAwait(false);
        }

        var dateMatch = Regex.Match(todayFeedContent, @"Дата:\s*(\d{2})\.(\d{2})\.(\d{4})");
        if (!dateMatch.Success)
        {
            throw new InvalidOperationException("[FATAL][WhatsApp] Malformed or missing Date in feed metadata. Fail-Closed.");
        }

        string editionDate = $"{dateMatch.Groups[3].Value}-{dateMatch.Groups[2].Value}-{dateMatch.Groups[1].Value}";
        Console.WriteLine($"[INFO][WhatsApp] Parsed EditionDate from feed: {editionDate}");

        var packages = new List<InputTerritoryPackage>();
        var rawTodayRecords = _parser.Parse(todayFeedContent);
        var parsedTodayRecords = await GetCumulativeTodayRecordsAsync(httpClient, editionDate, rawTodayRecords, isDryRun, cancellationToken).ConfigureAwait(false);

        var activeTerritoryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in parsedTodayRecords)
        {
            if (!string.IsNullOrWhiteSpace(r.TerritoryName))
            {
                try { activeTerritoryIds.Add(TerritoryRegistry.MapRawTerritoryName(r.TerritoryName)); }
                catch { activeTerritoryIds.Add(r.TerritoryName); }
            }
        }

        var historicalSettlements = new List<string>();
        if (historicalTerritoryIds != null)
        {
            foreach (var tid in historicalTerritoryIds)
            {
                if (activeTerritoryIds.Contains(tid)) continue;
                var canon = TerritoryRegistry.Territories.FirstOrDefault(t => t.TerritoryId.Equals(tid, StringComparison.OrdinalIgnoreCase));
                if (canon != null)
                {
                    string prefix = canon.Type == TerritoryType.CITY ? "м." : "с.";
                    string settlementName = $"{prefix} {canon.CenterSettlement}";
                    if (!historicalSettlements.Contains(settlementName))
                    {
                        historicalSettlements.Add(settlementName);
                    }
                }
            }
        }

        bool hasAnyOutagesToday = parsedTodayRecords.Count > 0 || historicalSettlements.Count > 0;
        if (!hasAnyOutagesToday)
        {
            string noOutagesText = WhatsAppContentFormatter.RenderNoOutagesPost(editionDate);
            byte[] noOutagesSvg = _bannerAssembly.AssembleWhatsAppNoOutagesSvg(editionDate);
            byte[] noOutagesPng = _rasterizer.RasterizeSvgToPng(noOutagesSvg, 1080, 600);
            packages.Add(new InputTerritoryPackage("journal_header", noOutagesText, noOutagesPng, true));
        }
        else
        {
            var stats = parsedTodayRecords.Count > 0
                ? TerritoryAggregator.CalculateSummaryStats(parsedTodayRecords)
                : new JournalSummaryStats(0, Array.Empty<string>(), Array.Empty<string>());

            var combinedPlanned = new List<string>(stats.PlannedSettlements);
            foreach (var hs in historicalSettlements)
            {
                if (!combinedPlanned.Contains(hs) && !stats.EmergencySettlements.Contains(hs))
                {
                    combinedPlanned.Add(hs);
                }
            }

            var cumulativeStats = new JournalSummaryStats(
                Math.Max(stats.TotalTerritories, combinedPlanned.Count + stats.EmergencySettlements.Count),
                combinedPlanned,
                stats.EmergencySettlements
            );

            string headerText = WhatsAppContentFormatter.RenderJournalHeader(editionDate, cumulativeStats);
            byte[] daySvg = _bannerAssembly.AssembleWhatsAppDayHeaderSvg(editionDate);
            byte[] dayPng = _rasterizer.RasterizeSvgToPng(daySvg, 1080, 480);
            packages.Add(new InputTerritoryPackage("journal_header", headerText, dayPng, true));

            if (parsedTodayRecords.Count > 0)
            {
                var aggregatedToday = TerritoryAggregator.AggregateByTerritory(parsedTodayRecords)
                    .OrderBy(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) || t.CanonicalName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

                foreach (var agg in aggregatedToday)
                {
                    string waTerritoryText = WhatsAppContentFormatter.RenderTerritoryPost(agg, isTomorrow: false);
                    packages.Add(new InputTerritoryPackage(agg.TerritoryId, waTerritoryText, null, true));
                }
            }
        }

        DateTime parsedToday = DateTime.Parse(editionDate);
        string tomorrowLabel = parsedToday.AddDays(1).ToString("yyyy-MM-dd");

        if (tomorrowAvailable && !string.IsNullOrWhiteSpace(tomorrowFeedContent))
        {
            if (tomorrowFeedContent.Contains("ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ") || tomorrowFeedContent.Contains("ЗНЕСТРУМЛЕННЯ"))
            {
                var tomRecords = _parser.Parse(tomorrowFeedContent);
                var tomAggregated = TerritoryAggregator.AggregateByTerritory(tomRecords);
                if (tomAggregated.Count > 0)
                {
                    var sortedTom = tomAggregated
                        .OrderBy(t => t.TerritoryId.Equals("starokostiantyniv", StringComparison.OrdinalIgnoreCase) || t.CanonicalName.Contains("Старокостянтинів", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(t => t.CanonicalName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                    var tomStats = TerritoryAggregator.CalculateSummaryStats(tomRecords);
                    string tomBannerCaption = WhatsAppContentFormatter.RenderJournalHeader(tomorrowLabel, tomStats);
                    if (!tomBannerCaption.StartsWith("*ПРОГНОЗ НА ЗАВТРА*", StringComparison.OrdinalIgnoreCase))
                    {
                        tomBannerCaption = $"*ПРОГНОЗ НА ЗАВТРА*\n\n{tomBannerCaption}";
                    }

                    byte[] tomSvg = _bannerAssembly.AssembleWhatsAppTomorrowHeaderSvg(tomorrowLabel);
                    byte[] tomPng = _rasterizer.RasterizeSvgToPng(tomSvg, 1080, 480);
                    packages.Add(new InputTerritoryPackage("tomorrow_separator", tomBannerCaption, tomPng, false));

                    foreach (var tAgg in sortedTom)
                    {
                        string waTomText = WhatsAppContentFormatter.RenderTerritoryPost(tAgg, isTomorrow: true, tomorrowDate: tomorrowLabel);
                        packages.Add(new InputTerritoryPackage($"tomorrow_{tAgg.TerritoryId}", waTomText, null, false));
                    }
                }
            }
        }

        GraphicInputPackage? graphicPackage = await FetchActiveGraphicScheduleAsync(httpClient, tomorrowLabel, editionDate, isDryRun, logPrefix: "[WhatsApp]", cancellationToken).ConfigureAwait(false);
        bool hasActiveTomorrow = packages.Any(p => p.TerritoryId.StartsWith("tomorrow", StringComparison.OrdinalIgnoreCase));

        return new EditorialInput(
            EditionDate: editionDate,
            Packages: packages,
            TomorrowForecastAvailable: hasActiveTomorrow,
            GraphicPackage: graphicPackage,
            SourceQueryTime: sourceQueryTime
        );
    }

    private async Task<GraphicInputPackage?> FetchActiveGraphicScheduleAsync(
        HttpClient httpClient,
        string tomorrowLabel,
        string editionDate,
        bool isDryRun,
        string logPrefix,
        CancellationToken cancellationToken)
    {
        if (isDryRun)
        {
            string sampleFixture = "local/fixtures/sample_graphic_schedule.json";
            if (File.Exists(sampleFixture))
            {
                try
                {
                    string fixtureJson = await File.ReadAllTextAsync(sampleFixture, cancellationToken).ConfigureAwait(false);
                    var parsedFixture = _parser.ParseLegacyGraphicJson(fixtureJson, "Старокостянтинівська МТГ");
                    if (parsedFixture.HasAnyOutages)
                    {
                        return parsedFixture;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN]{logPrefix} Failed to parse dry-run graphic fixture: {ex.Message}");
                }
            }
            return null;
        }

        // Priority 1: Check tomorrow's graphic schedule JSON (e.g., published in the evening for tomorrow)
        string onlineGraphicTomorrowUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{tomorrowLabel}.json";
        try
        {
            Console.WriteLine($"[INFO]{logPrefix} Checking graphic schedule JSON for tomorrow ({tomorrowLabel})...");
            string tomJson = await httpClient.GetStringAsync(onlineGraphicTomorrowUrl, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(tomJson))
            {
                var tomPkg = _parser.ParseLegacyGraphicJson(tomJson, "Старокостянтинівська МТГ");
                if (tomPkg.HasAnyOutages)
                {
                    Console.WriteLine($"[INFO]{logPrefix} Active 12-subqueue graphic schedule found for tomorrow ({tomorrowLabel}).");
                    return tomPkg;
                }
                Console.WriteLine($"[INFO]{logPrefix} Tomorrow's graphic JSON ({tomorrowLabel}) has no outages across all 12 subqueues. Skipping tomorrow graphic.");
            }
        }
        catch
        {
            // Tomorrow's graphic JSON not found (404), proceed to check today's graphic
        }

        // Priority 2: Fallback to today's graphic schedule JSON
        string onlineGraphicTodayUrl = $"https://raw.githubusercontent.com/realtomchuk-source/SvitloSk/main/parser/tg_posts/{editionDate}.json";
        try
        {
            Console.WriteLine($"[INFO]{logPrefix} Checking graphic schedule JSON for today ({editionDate})...");
            string todayJson = await httpClient.GetStringAsync(onlineGraphicTodayUrl, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(todayJson))
            {
                var todayPkg = _parser.ParseLegacyGraphicJson(todayJson, "Старокостянтинівська МТГ");
                if (todayPkg.HasAnyOutages)
                {
                    Console.WriteLine($"[INFO]{logPrefix} Active 12-subqueue graphic schedule found for today ({editionDate}).");
                    return todayPkg;
                }
                Console.WriteLine($"[INFO]{logPrefix} Today's graphic JSON ({editionDate}) is clean (no outages across all 12 subqueues). Skipping graphic publication.");
            }
        }
        catch (Exception gEx)
        {
            Console.WriteLine($"[INFO]{logPrefix} Online graphic JSON for {tomorrowLabel} / {editionDate} is not available ({gEx.Message}). Graphic schedule skipped.");
        }

        return null;
    }

    private async Task<IReadOnlyList<OutageRecord>> GetCumulativeTodayRecordsAsync(
        HttpClient httpClient,
        string editionDate,
        IReadOnlyList<OutageRecord> currentRecords,
        bool isDryRun,
        CancellationToken cancellationToken)
    {
        string cacheDir = Path.Combine("local", "registry");
        if (!Directory.Exists(cacheDir))
        {
            Directory.CreateDirectory(cacheDir);
        }

        string cacheFile = Path.Combine(cacheDir, $"facebook_daily_outages_{editionDate}.json");
        var cumulativeRecords = new List<OutageRecord>();

        // 1. Try to load from existing daily cache file
        if (File.Exists(cacheFile))
        {
            try
            {
                string json = await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<List<OutageRecord>>(json);
                if (loaded != null && loaded.Count > 0)
                {
                    cumulativeRecords = MergeRecords(cumulativeRecords, loaded);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN][Facebook] Failed to read daily outage cache ({cacheFile}): {ex.Message}");
            }
        }

        // 2. If cumulative records has no prior records or only 1 territory and we are online,
        // query GitHub commits on today.txt for the current editionDate to recover any earlier outages today.
        if (!isDryRun && cumulativeRecords.Count <= 1)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/realtomchuk-source/OutagesSk/commits?path=data/tg_posts/today.txt&per_page=10");
                req.Headers.Add("User-Agent", "SvitloSkPublisher");
                var resp = await httpClient.SendAsync(req, cancellationToken).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    string commitsJson = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(commitsJson);
                    foreach (var elem in doc.RootElement.EnumerateArray())
                    {
                        string? commitDate = elem.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
                        string? sha = elem.GetProperty("sha").GetString();
                        if (!string.IsNullOrEmpty(sha) && commitDate != null && commitDate.StartsWith(editionDate))
                        {
                            try
                            {
                                string rawUrl = $"https://raw.githubusercontent.com/realtomchuk-source/OutagesSk/{sha}/data/tg_posts/today.txt";
                                string historicalFeed = await httpClient.GetStringAsync(rawUrl, cancellationToken).ConfigureAwait(false);
                                if (!string.IsNullOrWhiteSpace(historicalFeed))
                                {
                                    var histRecords = _parser.Parse(historicalFeed);
                                    cumulativeRecords = MergeRecords(cumulativeRecords, histRecords);
                                }
                            }
                            catch
                            {
                                // Ignore failure on individual commit raw download
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[INFO][Facebook] GitHub commit check bypassed: {ex.Message}");
            }
        }

        // 3. Merge current incoming records
        cumulativeRecords = MergeRecords(cumulativeRecords, currentRecords);

        // 4. Save merged records back to cache file
        try
        {
            string outJson = System.Text.Json.JsonSerializer.Serialize(cumulativeRecords, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, outJson, cancellationToken).ConfigureAwait(false);

            // Clean up older cache files
            foreach (var f in Directory.GetFiles(cacheDir, "facebook_daily_outages_*.json"))
            {
                string fn = Path.GetFileName(f);
                if (!fn.Contains(editionDate) && File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-2))
                {
                    try { File.Delete(f); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN][Facebook] Failed to write daily outage cache: {ex.Message}");
        }

        return cumulativeRecords;
    }

    private static List<OutageRecord> MergeRecords(IEnumerable<OutageRecord> existing, IEnumerable<OutageRecord> incoming)
    {
        var merged = new List<OutageRecord>(existing);

        foreach (var inc in incoming)
        {
            if (string.IsNullOrWhiteSpace(inc.TerritoryName) || string.IsNullOrWhiteSpace(inc.Details))
                continue;
            if (inc.Details.Contains("не зафіксовано", StringComparison.OrdinalIgnoreCase) ||
                inc.Details.Contains("не заплановано", StringComparison.OrdinalIgnoreCase))
                continue;

            bool alreadyExists = merged.Any(e =>
                string.Equals(e.TerritoryName, inc.TerritoryName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(e.OutageType, inc.OutageType, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(e.Details.Trim(), inc.Details.Trim(), StringComparison.OrdinalIgnoreCase) ||
                 e.Details.Length >= inc.Details.Length));

            if (!alreadyExists)
            {
                merged.RemoveAll(e =>
                    string.Equals(e.TerritoryName, inc.TerritoryName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.OutageType, inc.OutageType, StringComparison.OrdinalIgnoreCase) &&
                    e.Details.Length < inc.Details.Length);

                merged.Add(inc);
            }
        }

        return merged;
    }
}
