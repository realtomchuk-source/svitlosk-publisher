using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using SvitloSk.Publisher.Infrastructure.Git;
using SvitloSk.Publisher.Infrastructure.Persistence;

namespace SvitloSk.Publisher.Infrastructure;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("[START] SvitloSk Publisher Sync Cycle");

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("[CANCEL] Cancellation requested by user.");
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            // 1. Load Configuration from Environment Variables
            string? botToken = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            string? chatNameOrId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID");
            string? registryPath = Environment.GetEnvironmentVariable("REGISTRY_PATH");

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

            // 2. Instantiate dependencies (Composition Root)
            using var httpClient = new HttpClient();
            var atomicWriter = new FileSystemAtomicWriter();
            var registryStore = new JsonRegistryStore(atomicWriter);
            var gitTransport = new GitTransport();
            var telegramAdapter = new TelegramAdapter(httpClient, botToken);
            var delayProvider = new SystemDelayProvider();
            var dispatcher = new SequentialDispatcher(telegramAdapter, delayProvider);
            var hashCalculator = new ContentHashCalculator();
            var decisionEngine = new EditorialDecisionEngine();

            var orchestrator = new PublisherOrchestrator(
                registryStore,
                gitTransport,
                hashCalculator,
                decisionEngine,
                dispatcher
            );

            // 3. Mock EditorialInput for today (production inputs will come from sources)
            var input = new EditorialInput(
                EditionDate: DateTime.UtcNow.ToString("yyyy-MM-dd"),
                Packages: new List<InputTerritoryPackage>
                {
                    new InputTerritoryPackage("svitlovodsk", "Світловодськ: Планові відключення відсутні.", null, true)
                }
            );

            // 4. Run Orchestration
            Console.WriteLine($"[INFO] Executing orchestration for registry '{registryPath}' and channel '{chatNameOrId}'");
            var result = await orchestrator.RunOrchestrationAsync(registryPath, chatNameOrId, input, cts.Token).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                Console.WriteLine($"[SUCCESS] Sync cycle completed. Total operations: {result.TotalProcessed}. Successful: {result.TotalSuccessful}.");
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
            return 99;
        }
    }
}
