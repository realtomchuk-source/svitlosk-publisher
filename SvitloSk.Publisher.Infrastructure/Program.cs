using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Facebook;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using SvitloSk.Publisher.Infrastructure.Git;
using SvitloSk.Publisher.Infrastructure.Graphics;
using SvitloSk.Publisher.Infrastructure.Persistence;
using SvitloSk.Publisher.Infrastructure.Services;

namespace SvitloSk.Publisher.Infrastructure;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        bool isDryRun = false;
        bool forceInit = false;
        bool runInitializeRegistry = false;
        bool runRegistryStatus = false;
        bool runRegistryVerify = false;
        bool runDiagnostics = false;
        bool runTelegramCheck = false;
        bool runFacebookCheck = false;
        bool runFacebookTestPost = false;
        bool runFeedCheck = false;

        foreach (var arg in args)
        {
            if (arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)) isDryRun = true;
            else if (arg.Equals("--init-registry", StringComparison.OrdinalIgnoreCase)) runInitializeRegistry = true;
            else if (arg.Equals("--force-init", StringComparison.OrdinalIgnoreCase)) forceInit = true;
            else if (arg.Equals("--registry-status", StringComparison.OrdinalIgnoreCase)) runRegistryStatus = true;
            else if (arg.Equals("--registry-verify", StringComparison.OrdinalIgnoreCase)) runRegistryVerify = true;
            else if (arg.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase)) runDiagnostics = true;
            else if (arg.Equals("--telegram-check", StringComparison.OrdinalIgnoreCase)) runTelegramCheck = true;
            else if (arg.Equals("--facebook-check", StringComparison.OrdinalIgnoreCase)) runFacebookCheck = true;
            else if (arg.Equals("--facebook-test-post", StringComparison.OrdinalIgnoreCase)) runFacebookTestPost = true;
            else if (arg.Equals("--feed-check", StringComparison.OrdinalIgnoreCase)) runFeedCheck = true;
        }

        const string MutexName = @"Global\SvitloSk_Publisher_Instance_Lock";
        using var appMutex = new Mutex(false, MutexName);

        if (!appMutex.WaitOne(0, false))
        {
            Console.WriteLine("[INFO] Another instance of SvitloSk.Publisher is already executing. Graceful exit.");
            return 0;
        }

        try
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            Console.WriteLine("[START] SvitloSk Publisher Sync Cycle");
            Console.WriteLine($"MODE: {(isDryRun ? "DRY-RUN" : "PRODUCTION")}");

            // 1. Environment & Configuration
            string? botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
                ?? Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN", EnvironmentVariableTarget.User);
            string? chatNameOrId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
                ?? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID", EnvironmentVariableTarget.User);
            string? discussionGroupId = Environment.GetEnvironmentVariable("TELEGRAM_DISCUSSION_GROUP_ID")
                ?? Environment.GetEnvironmentVariable("TELEGRAM_DISCUSSION_GROUP_ID", EnvironmentVariableTarget.User);
            string? registryPath = Environment.GetEnvironmentVariable("REGISTRY_PATH")
                ?? Environment.GetEnvironmentVariable("REGISTRY_PATH", EnvironmentVariableTarget.User);

            if (isDryRun)
            {
                botToken ??= "dry_run_token";
                chatNameOrId ??= "dryrun";
                registryPath = Path.Combine(Path.GetTempPath(), "svitlosk_dry_run_registry.json");
                Console.WriteLine($"[DryRun] Isolated registry path: {registryPath}");
            }
            else if (!runFacebookCheck && !runFacebookTestPost)
            {
                if (string.IsNullOrWhiteSpace(botToken))
                {
                    Console.Error.WriteLine("[FATAL] Missing required configuration: TELEGRAM_BOT_TOKEN environment variable.");
                    return 1;
                }
                if (string.IsNullOrWhiteSpace(chatNameOrId))
                {
                    Console.Error.WriteLine("[FATAL] Missing required configuration: TELEGRAM_CHAT_ID environment variable.");
                    return 1;
                }
                if (string.IsNullOrWhiteSpace(registryPath))
                {
                    Console.Error.WriteLine("[FATAL] Missing required configuration: REGISTRY_PATH environment variable.");
                    return 1;
                }
            }

            // 1.1 Facebook Environment & Configuration
            string? fbPageId = Environment.GetEnvironmentVariable("FACEBOOK_PAGE_ID")
                ?? Environment.GetEnvironmentVariable("FACEBOOK_PAGE_ID", EnvironmentVariableTarget.User);
            string? fbToken = Environment.GetEnvironmentVariable("FACEBOOK_PAGE_ACCESS_TOKEN")
                ?? Environment.GetEnvironmentVariable("FACEBOOK_PAGE_ACCESS_TOKEN", EnvironmentVariableTarget.User);
            string? fbRegistryPath = Environment.GetEnvironmentVariable("FACEBOOK_REGISTRY_PATH")
                ?? Environment.GetEnvironmentVariable("FACEBOOK_REGISTRY_PATH", EnvironmentVariableTarget.User);

            if (isDryRun)
            {
                fbPageId ??= "fb_dryrun_page";
                fbToken ??= "fb_dryrun_token";
                fbRegistryPath = Path.Combine(Path.GetTempPath(), "svitlosk_dry_run_facebook_registry.json");
            }
            else
            {
                fbRegistryPath ??= "local/registry/facebook_registry.json";
            }

            // 2. Composition Root
            using var httpClient = new HttpClient();
            var atomicWriter = new FileSystemAtomicWriter();
            var registryStore = new JsonRegistryStore(atomicWriter);
            IGitTransport gitTransport = isDryRun ? new DryRunGitTransport() : new GitTransport();

            ITelegramAdapter telegramAdapter = isDryRun 
                ? new FakeTelegramDryRunAdapter() 
                : new TelegramAdapter(httpClient, botToken);
            Console.WriteLine($"NETWORK: {(isDryRun ? "DISABLED" : "ENABLED")}");

            var delayProvider = new SystemDelayProvider();
            var hashCalculator = new ContentHashCalculator();
            var decisionEngine = new EditorialDecisionEngine();
            var parser = new OutageFeedParser();
            var bannerAssembly = new BannerGraphicAssembly();
            var rasterizer = new SvgSkiaRasterizer();
            var transformer = new EditorialContentTransformer(bannerAssembly, rasterizer);

            IGraphicPublisherDispatcher? graphicDispatcher = isDryRun
                ? new FakeGraphicDryRunDispatcher()
                : new TelegramGraphicPublisherDispatcher(httpClient, botToken, delayProvider);

            var discussionManager = isDryRun || string.IsNullOrWhiteSpace(botToken) 
                ? null 
                : new TelegramDiscussionManager(httpClient, botToken);

            IChannelPipeline pipeline = new TelegramPipeline(
                telegramAdapter,
                chatNameOrId,
                discussionGroupId,
                graphicDispatcher,
                new TelegramRateLimiter(delayProvider),
                discussionManager
            );

            IFacebookAdapter? facebookAdapter = isDryRun
                ? new FacebookDryRunAdapter()
                : (!string.IsNullOrWhiteSpace(fbToken) && !string.IsNullOrWhiteSpace(fbPageId)
                    ? new FacebookGraphApiClient(httpClient, fbToken)
                    : null);

            IChannelPipeline? facebookPipeline = (facebookAdapter != null && !string.IsNullOrWhiteSpace(fbPageId))
                ? new FacebookPipeline(facebookAdapter, fbPageId, rasterizer, bannerAssembly)
                : null;

            var orchestrator = new PublisherOrchestrator(
                registryStore,
                gitTransport,
                hashCalculator,
                decisionEngine,
                pipeline,
                parser,
                transformer
            );

            var diagnosticsService = new PublisherDiagnosticsService();

            // 3. Diagnostic & Maintenance CLI Commands
            if (runInitializeRegistry)
                return await diagnosticsService.RunInitializeRegistryAsync(registryPath, forceInit, registryStore, cts.Token);

            if (runRegistryStatus)
                return await diagnosticsService.RunRegistryStatusAsync(registryPath, registryStore, cts.Token);

            if (runRegistryVerify)
                return await diagnosticsService.RunRegistryVerifyAsync(registryPath, registryStore, cts.Token);

            if (runDiagnostics)
                return await diagnosticsService.RunDiagnosticsAsync(registryPath, botToken, chatNameOrId, gitTransport, cts.Token);

            if (runTelegramCheck)
                return await diagnosticsService.RunTelegramCheckAsync(httpClient, botToken, cts.Token);

            if (runFacebookCheck)
            {
                if (string.IsNullOrWhiteSpace(fbToken) || string.IsNullOrWhiteSpace(fbPageId))
                {
                    Console.Error.WriteLine("[FATAL] Facebook check requires FACEBOOK_PAGE_ID and FACEBOOK_PAGE_ACCESS_TOKEN environment variables.");
                    return 1;
                }
                var fbClient = new FacebookGraphApiClient(httpClient, fbToken);
                var chkResult = await fbClient.CheckPageAccessAsync(fbPageId, cts.Token);
                if (chkResult.IsSuccess)
                {
                    Console.WriteLine($"[SUCCESS] {chkResult.ErrorDescription}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"[ERROR] Facebook page check failed: {chkResult.ErrorDescription}");
                    return 1;
                }
            }

            if (runFacebookTestPost)
            {
                if (string.IsNullOrWhiteSpace(fbToken) || string.IsNullOrWhiteSpace(fbPageId))
                {
                    Console.Error.WriteLine("[FATAL] Facebook test post requires FACEBOOK_PAGE_ID and FACEBOOK_PAGE_ACCESS_TOKEN environment variables.");
                    return 1;
                }

                Console.WriteLine("[INFO] Assembling Facebook Day Header banner (1200x630)...");
                string todayDate = DateTime.Now.ToString("dd.MM.yyyy");
                byte[] svgBytes = bannerAssembly.AssembleFacebookDayHeaderSvg(todayDate);
                byte[] pngBytes = rasterizer.RasterizeSvgToPng(svgBytes, 1200, 630);

                string testText = "💡 ЖУРНАЛ ЗНЕСТРУМЛЕНЬ | СТАРОКОСТЯНТИНІВСЬКА МІСЬКА ТЕРИТОРІАЛЬНА ГРОМАДА\n\n" +
                                  $"Тестове підключення автоматичного паблішера SvitloSk Journal ({todayDate}).\n" +
                                  "Електропостачання та графіки публікуються в автоматичному режимі.";

                Console.WriteLine($"[INFO] Publishing test photo post to Facebook Page '{fbPageId}' via Graph API...");
                var fbClient = new FacebookGraphApiClient(httpClient, fbToken);
                var postResult = await fbClient.PublishPostAsync(fbPageId, testText, pngBytes, cts.Token);

                if (postResult.IsSuccess)
                {
                    Console.WriteLine($"[SUCCESS] Test post published successfully to Facebook Page!");
                    Console.WriteLine($"[INFO] Post ID: {postResult.PostId}");
                    Console.WriteLine($"[INFO] Check your page: https://www.facebook.com/{fbPageId}");
                    return 0;
                }
                else
                {
                    Console.Error.WriteLine($"[ERROR] Facebook test post failed: {postResult.ErrorDescription}");
                    return 1;
                }
            }

            if (runFeedCheck)
                return await diagnosticsService.RunFeedCheckAsync(httpClient, parser, cts.Token);

            // 4. Ingest Editorial Feed
            var ingestionService = new FeedIngestionService(parser, bannerAssembly, rasterizer, transformer);
            var input = await ingestionService.IngestEditorialInputAsync(httpClient, isDryRun, cts.Token);

            // 5. Run Orchestration
            Console.WriteLine($"[INFO] Executing orchestration for registry '{registryPath}' and channel '{chatNameOrId}' (DiscussionGroup: {discussionGroupId ?? "NONE"})");

            var watch = Stopwatch.StartNew();
            var result = await orchestrator.RunOrchestrationAsync(registryPath, chatNameOrId, input, discussionGroupId, cts.Token).ConfigureAwait(false);
            watch.Stop();

            int createCount = 0, updateCount = 0, deleteCount = 0, noopCount = 0;
            foreach (var res in result.Results)
            {
                if (res.DecisionResult == "Create") createCount++;
                else if (res.DecisionResult == "Update") updateCount++;
                else if (res.DecisionResult == "Delete") deleteCount++;
                else noopCount++;
            }

            string logMessage = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] RunId: {Guid.NewGuid():N} | Source: online/fallback | EditionDate: {input.EditionDate} | " +
                               $"CREATE: {createCount} | UPDATE: {updateCount} | DELETE: {deleteCount} | NOOP: {noopCount} | " +
                               $"IsSuccess: {result.IsSuccess} | Duration: {watch.ElapsedMilliseconds}ms | ExitCode: {(result.IsSuccess ? 0 : 2)}\n";

            try
            {
                Directory.CreateDirectory("local/logs/publisher");
                File.AppendAllText("local/logs/publisher/sync.log", logMessage);
            }
            catch (Exception logEx)
            {
                Console.Error.WriteLine($"[WARN] Could not write to local operational log: {logEx.Message}");
            }

            if (result.IsSuccess)
            {
                Console.WriteLine($"[SUCCESS] Telegram sync cycle completed. Total operations: {result.TotalProcessed}. Successful: {result.TotalSuccessful}.");
                Console.WriteLine($"[INFO] Metrics -> CREATE: {createCount} | UPDATE: {updateCount} | DELETE: {deleteCount} | NOOP: {noopCount}");

                // 6. Facebook Channel Synchronization (Isolated Blast Radius)
                if (facebookPipeline != null && !string.IsNullOrWhiteSpace(fbRegistryPath) && !string.IsNullOrWhiteSpace(fbPageId))
                {
                    Console.WriteLine($"\n[START] Facebook Channel Synchronization");
                    Console.WriteLine($"[INFO] Facebook Registry: '{fbRegistryPath}', Page ID: '{fbPageId}'");
                    try
                    {
                        var fbOrchestrator = new PublisherOrchestrator(
                            registryStore,
                            gitTransport,
                            hashCalculator,
                            decisionEngine,
                            facebookPipeline,
                            parser,
                            transformer
                        );

                        var fbWatch = Stopwatch.StartNew();
                        var fbResult = await fbOrchestrator.RunOrchestrationAsync(fbRegistryPath, fbPageId, input, null, cts.Token).ConfigureAwait(false);
                        fbWatch.Stop();

                        int fbCreate = fbResult.Results.Count(r => r.DecisionResult == "Create");
                        int fbUpdate = fbResult.Results.Count(r => r.DecisionResult == "Update");
                        int fbDelete = fbResult.Results.Count(r => r.DecisionResult == "Delete");
                        int fbNoop = fbResult.Results.Count(r => r.DecisionResult is not ("Create" or "Update" or "Delete"));

                        if (fbResult.IsSuccess)
                        {
                            Console.WriteLine($"[SUCCESS] Facebook sync completed in {fbWatch.ElapsedMilliseconds}ms. Total: {fbResult.TotalProcessed}, Successful: {fbResult.TotalSuccessful}.");
                            Console.WriteLine($"[INFO][Facebook] Metrics -> CREATE: {fbCreate} | UPDATE: {fbUpdate} | DELETE: {fbDelete} | NOOP: {fbNoop}");
                        }
                        else
                        {
                            Console.Error.WriteLine($"[WARN][Facebook] Sync encountered issues: {fbResult.FatalErrorDescription}");
                        }
                    }
                    catch (Exception fbEx)
                    {
                        Console.Error.WriteLine($"[ERROR][Facebook] Non-fatal exception in Facebook channel: {fbEx.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("[INFO] Facebook channel is not configured, skipping.");
                }

                return 0;
            }
            else
            {
                Console.Error.WriteLine($"[ERROR] Batch synchronization failed: {result.FatalErrorDescription}");
                return 2;
            }
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("[ERROR] Synchronization run cancelled by user.");
            return 3;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] Unhandled infrastructure exception: {ex.Message}");
            try
            {
                Directory.CreateDirectory("local/logs/publisher");
                File.AppendAllText("local/logs/publisher/sync.log", $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC] FATAL ERROR: {ex.Message} | ExitCode: 99\n");
            }
            catch { }
            return 99;
        }
    }
}
