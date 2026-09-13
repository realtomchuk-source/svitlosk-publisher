using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using SvitloSk.Publisher.Infrastructure.Git;
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
            string? botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            string? chatNameOrId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            string? discussionGroupId = Environment.GetEnvironmentVariable("TELEGRAM_DISCUSSION_GROUP_ID");
            string? registryPath = Environment.GetEnvironmentVariable("REGISTRY_PATH");

            if (isDryRun)
            {
                botToken ??= "dry_run_token";
                chatNameOrId ??= "dryrun";
                registryPath = Path.Combine(Path.GetTempPath(), "svitlosk_dry_run_registry.json");
                Console.WriteLine($"[DryRun] Isolated registry path: {registryPath}");
            }
            else
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

            var orchestrator = new PublisherOrchestrator(
                registryStore,
                gitTransport,
                hashCalculator,
                decisionEngine,
                pipeline,
                parser,
                transformer,
                graphicDispatcher
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
                Console.WriteLine($"[SUCCESS] Sync cycle completed. Total operations: {result.TotalProcessed}. Successful: {result.TotalSuccessful}.");
                Console.WriteLine($"[INFO] Metrics -> CREATE: {createCount} | UPDATE: {updateCount} | DELETE: {deleteCount} | NOOP: {noopCount}");
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
