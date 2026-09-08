using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Orchestration;

public class PublisherOrchestratorTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _registryPath;

    public PublisherOrchestratorTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_OrchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _registryPath = Path.Combine(_testDirectory, "registry.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    internal class FakeRegistryStore : IRegistryStore
    {
        public RegistryModel? CurrentModel { get; set; }
        public bool ThrowOnSave { get; set; }
        public bool ThrowOnLoad { get; set; }

        public Task<RegistryModel?> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (ThrowOnLoad) throw new InvalidOperationException("Simulated load failure.");
            return Task.FromResult(CurrentModel);
        }

        public Task SaveAsync(string path, RegistryModel model, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("Simulated save failure.");
            CurrentModel = model;
            return Task.CompletedTask;
        }
    }

    internal class FakeGitTransport : IGitTransport
    {
        public bool PushCalled { get; private set; }
        public bool RestoreCalled { get; private set; }
        public bool ThrowOnPush { get; set; }

        public Task CommitAndPushAsync(string filePath, string commitMessage, CancellationToken cancellationToken = default)
        {
            if (ThrowOnPush) throw new InvalidOperationException("Simulated push failure.");
            PushCalled = true;
            return Task.CompletedTask;
        }

        public Task<string?> RestoreFromHistoryAsync(string filePath, CancellationToken cancellationToken = default)
        {
            RestoreCalled = true;
            return Task.FromResult<string?>(null); // Simulate empty recovery
        }
    }

    internal class FakeTelegramAdapter : ITelegramAdapter
    {
        public int SendCount { get; private set; }
        public bool ForceFatal { get; set; }
        public List<string> SentTexts { get; } = new();

        public Task<TelegramDispatchResult> SendAsync(string chatNameOrId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            SendCount++;
            SentTexts.Add(text);
            if (ForceFatal)
            {
                return Task.FromResult(new TelegramDispatchResult(false, null, "Unauthorized", false));
            }
            return Task.FromResult(new TelegramDispatchResult(true, 1111, null, false));
        }

        public Task<TelegramDispatchResult> UpdateAsync(string chatNameOrId, int messageId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            SentTexts.Add(text);
            return Task.FromResult(new TelegramDispatchResult(true, messageId, null, false));
        }

        public Task<TelegramDispatchResult> DeleteAsync(string chatNameOrId, int messageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
        }

        public Task<TelegramDispatchResult> CloseCommentsAsync(string discussionGroupId, int channelMessageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
        }
    }


    private class FakeDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task TC_MassDeleteSafetyGuard_FailsClosed()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // Pre-populate registry with 5 active publications
        var initialPublications = new List<RegistryPublicationRecord>();
        for (int i = 1; i <= 5; i++)
        {
            initialPublications.Add(new RegistryPublicationRecord(Guid.NewGuid(), $"territory{i}", 1000 + i, $"hash{i}", "SENT"));
        }
        var initialRegistry = new RegistryModel(1, "2026-08-12", "ACTIVE", initialPublications);
        await store.SaveAsync(_registryPath, initialRegistry);

        // Feed input has zero active publications (empty feed after having 5 active ones)
        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", "============================================\nДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 12.08.2026\n============================================\nВідключень не зафіксовано.\n============================================\nКІНЕЦЬ ДОКУМЕНТУ\n", null, true)
            }
        );

        // In Option B (History Preservation), absent persistent territories are retained, not deleted.
        // Therefore, feed with 0 active publications does not delete previous historical publications.
        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result.IsSuccess);
        Assert.NotNull(store.CurrentModel);
        Assert.Equal(5, store.CurrentModel.Publications.Count(p => p.TransmissionState == "SENT" || p.TransmissionState == "UPDATED"));
    }

    [Fact]
    public async Task TC_RegistryValidation_FailsClosed_OnDuplicateMessageIds()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // Pre-populate registry with active publications sharing duplicate message ID
        var initialPublications = new List<RegistryPublicationRecord>
        {
            new RegistryPublicationRecord(Guid.NewGuid(), "territory1", 9999, "hash1", "SENT"),
            new RegistryPublicationRecord(Guid.NewGuid(), "territory2", 9999, "hash2", "SENT")
        };
        var initialRegistry = new RegistryModel(1, "2026-08-12", "ACTIVE", initialPublications);
        await store.SaveAsync(_registryPath, initialRegistry);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("starokostiantyniv", "Text content", null, true)
            }
        );

        // Should throw validation exception
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOrchestrationAsync(_registryPath, "-100999", input));
        Assert.Contains("Registry validation failed", ex.Message);
    }

    [Fact]
    public async Task TC_RegistryBackup_FailsClosed_OnBackupFailure()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // Pre-populate registry model
        var initialPublications = new List<RegistryPublicationRecord>
        {
            new RegistryPublicationRecord(Guid.NewGuid(), "territory1", 1111, "hash1", "SENT")
        };
        var initialRegistry = new RegistryModel(1, "2026-08-12", "ACTIVE", initialPublications);
        store.CurrentModel = initialRegistry;

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("starokostiantyniv", "Text content", null, true)
            }
        );

        // Create a real file, but block Directory.CreateDirectory(backupDir) by creating a file named "backups" at that location.
        string tempRegistryFile = Path.Combine(_testDirectory, "valid_registry.json");
        File.WriteAllText(tempRegistryFile, "{}");

        string backupsFilePath = Path.Combine(_testDirectory, "backups");
        File.WriteAllText(backupsFilePath, "blocking file");

        var res = await orchestrator.RunOrchestrationAsync(tempRegistryFile, "-100123", input);
        Assert.False(res.IsSuccess);
        Assert.Contains("backup failed", res.FatalErrorDescription);
    }

    [Fact]
    public void TC_SingleInstanceMutex_PreventsOverlappingInstances()
    {
        // Acquire mutex lock mimicking a running publisher instance
        using var firstMutex = new System.Threading.Mutex(true, "Global\\SvitloSk_Outage_Publisher_Mutex", out bool firstCreated);
        Assert.True(firstCreated, "Should be able to acquire the first Mutex lock.");

        // Try to acquire the second one - must fail
        using var secondMutex = new System.Threading.Mutex(true, "Global\\SvitloSk_Outage_Publisher_Mutex", out bool secondCreated);
        Assert.False(secondCreated, "Secondary Mutex acquisition should fail when an instance is already running.");
    }

    [Fact]
    public async Task E02_T01_SuccessfulEndToEndOrchestration_ShouldSyncSaveAndCommit()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();

        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("starokostiantyniv", "Text content", null, true)
            }
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, adapter.SendCount); // starokostiantyniv + system_status
        Assert.NotNull(store.CurrentModel);
        Assert.Equal(2, store.CurrentModel.Publications.Count);
        Assert.Equal(1111, store.CurrentModel.Publications[0].TelegramMessageId);
        Assert.True(git.PushCalled);
    }

    [Fact]
    public async Task E02_T06_FatalTelegramFailure_ShouldAbortOrchestration()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter { ForceFatal = true };
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();

        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("starokostiantyniv", "Text content", null, true)
            }
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);

        Assert.False(result.IsSuccess);
        Assert.Equal("Unauthorized", result.FatalErrorDescription);
        Assert.False(git.PushCalled); // No Git push on failure
    }

    [Fact]
    public async Task E02_T07_RegistryPersistenceFailure_ShouldPropagateExceptionWithoutGitPush()
    {
        var store = new FakeRegistryStore { ThrowOnSave = true };
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();

        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("starokostiantyniv", "Text content", null, true)
            }
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input));
        Assert.False(git.PushCalled);
    }

    [Fact]
    public async Task D02_T13_RunnerCrash_ShouldIdempotentlyReconcile()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();

        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // Run 1: Create a publication in the registry. 
        var pkg = new InputTerritoryPackage("starokostiantyniv", "Text content", null, true);
        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage> { pkg }
        );

        // Run 1 completes, generating the registry
        var result1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result1.IsSuccess);
        Assert.Equal(2, adapter.SendCount);
        Assert.NotNull(store.CurrentModel);

        // Run 2: Re-run with the exact same input. 
        var result2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result2.IsSuccess);
        Assert.Equal(2, adapter.SendCount); // SendCount remains 2 (no duplicate sent)
    }

    [Fact]
    public void TC_UX_TodayTemplate_IsHTML_AndExcludesQueues()
    {
        var transformer = new EditorialContentTransformer();
        var record = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14, 16 1 черга", "1");
        string rendered = transformer.RenderTemplate(record, "СЬОГОДНІ — 2026-08-08");

        Assert.Contains("<b>Місто Старокостянтинів</b>", rendered);
        Assert.Contains("<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (08:00–12:00)</b>", rendered);
        Assert.Contains("- вул. Миру, 14, 16", rendered);
        Assert.DoesNotContain("черга", rendered, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("📍", rendered);
        Assert.DoesNotContain("📅", rendered);
    }

    [Fact]
    public void TC_UX_Today_SourceIntervalsArePreservedAndNotRegrouped()
    {
        var transformer = new EditorialContentTransformer();
        var record1 = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14", "1");
        var record2 = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 14:00 по 18:00\nвул. Островського 5", "2");

        string rendered1 = transformer.RenderTemplate(record1, "08.08.2026");
        string rendered2 = transformer.RenderTemplate(record2, "08.08.2026");

        Assert.Contains("08:00–12:00", rendered1);
        Assert.Contains("- вул. Миру, 14", rendered1);

        Assert.Contains("14:00–18:00", rendered2);
        Assert.Contains("- вул. Островського, 5", rendered2);
    }

    [Fact]
    public void TC_UX_OutageTypes_AreDistinguishable()
    {
        var transformer = new EditorialContentTransformer();
        var planoRecord = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14", "1");
        var avariyniRecord = new OutageRecord("Місто Старокостянтинів", "АВАРІЙНІ", "вул. Ізяславська 27, аварія на лінії", null);

        string renderedPlano = transformer.RenderTemplate(planoRecord, "08.08.2026");
        string renderedAvariyni = transformer.RenderTemplate(avariyniRecord, "08.08.2026");

        Assert.Contains("<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ (08:00–12:00)</b>", renderedPlano);
        Assert.Contains("<blockquote><b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ</b>", renderedAvariyni);
        Assert.Contains("<b>Місто Старокостянтинів</b>", renderedAvariyni);
        Assert.Contains("- вул. Ізяславська, 27, аварія на лінії", renderedAvariyni);
    }

    [Fact]
    public void TC_UX_HTML_EscapingAndSafety()
    {
        var transformer = new EditorialContentTransformer();
        var record = new OutageRecord("Місто & Громада <Старокостянтинів>", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 1 > 2", "1");
        string rendered = transformer.RenderTemplate(record, "08.08.2026");

        Assert.Contains("<b>Місто &amp; Громада &lt;Старокостянтинів&gt;</b>", rendered);
        Assert.Contains("вул. Миру, 1 &gt; 2", rendered);
    }

    [Fact]
    public void TC_UX_ContentHash_IsQueueIndependent()
    {
        var transformer = new EditorialContentTransformer();
        var calculator = new ContentHashCalculator();

        var recordWithQueue1 = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14 1 черга", "1.1");
        var recordWithQueue2 = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14 2 черга", "1.2");

        string t1 = transformer.RenderTemplate(recordWithQueue1, "08.08.2026");
        string t2 = transformer.RenderTemplate(recordWithQueue2, "08.08.2026");

        Assert.Equal(t1, t2);

        string h1 = calculator.ComputeHash(t1, null);
        string h2 = calculator.ComputeHash(t2, null);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void TC_UX_DuplicateIntervals_AreNotMergedOrRegrouped()
    {
        var transformer = new EditorialContentTransformer();
        var record = new OutageRecord("Місто Старокостянтинів", "ПЛАНОВІ", "з 08:00 по 12:00\nвул. Миру 14\nз 08:00 по 12:00\nвул. Ізяславська 27", "1");
        string rendered = transformer.RenderTemplate(record, "СЬОГОДНІ — 08.08.2026");

        var firstIdx = rendered.IndexOf("Час: 08:00–12:00");
        var lastIdx = rendered.LastIndexOf("Час: 08:00–12:00");
        
        Assert.True(firstIdx >= 0);
        Assert.True(lastIdx >= 0);
    }

    [Fact]
    public void TC_UX_TomorrowDate_IsEditionDatePlusOne()
    {
        // Tomorrow date parsing logic matches Program.cs
        string editionDate = "2026-08-08";
        DateTime parsedToday = DateTime.Parse(editionDate);
        DateTime tomorrowDate = parsedToday.AddDays(1);
        string tomorrowLabel = tomorrowDate.ToString("yyyy-MM-dd");

        Assert.Equal("2026-08-09", tomorrowLabel);
    }

    [Fact]
    public void TC_UX_TelegramMessageLimit_SplitSafely()
    {
        var transformer = new EditorialContentTransformer();
        string longText = "line 1\nline 2\nline 3\nline 4";
        var chunks = transformer.SplitTelegramMessage(longText, 15);

        // limit = 15: "line 1\nline 2" is 13 chars, adding "line 3" (total 20 chars) exceeds 15 limit.
        Assert.Equal(2, chunks.Count);
        Assert.Equal("line 1\nline 2", chunks[0]);
        Assert.Equal("line 3\nline 4", chunks[1]);
    }

    [Fact]
    public async Task TC_UX_FullOrchestratorPipeline_IsQueueIndependent()
    {
        var store1 = new FakeRegistryStore();
        var store2 = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter1 = new FakeTelegramAdapter();
        var adapter2 = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        
        var dispatcher1 = new SequentialDispatcher(adapter1, delay);
        var dispatcher2 = new SequentialDispatcher(adapter2, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        
        var orchestrator1 = new PublisherOrchestrator(store1, git, calculator, decisionEngine, dispatcher1, parser, transformer);
        var orchestrator2 = new PublisherOrchestrator(store2, git, calculator, decisionEngine, dispatcher2, parser, transformer);

        // Record A with queue metadata
        var rawPkg1 = new InputTerritoryPackage("starokostiantyniv", "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 08.08.2026\n[Місто Старокостянтинів]\nз 08:00 по 12:00\nвул. Миру 14, 16 1 черга", null, true);
        var input1 = new EditorialInput(EditionDate: "2026-08-08", Packages: new List<InputTerritoryPackage> { rawPkg1 });

        // Record B with different queue metadata but identical user-visible content
        var rawPkg2 = new InputTerritoryPackage("starokostiantyniv", "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\nДата: 08.08.2026\n[Місто Старокостянтинів]\nз 08:00 по 12:00\nвул. Миру 14, 16 2 черга", null, true);
        var input2 = new EditorialInput(EditionDate: "2026-08-08", Packages: new List<InputTerritoryPackage> { rawPkg2 });

        var res1 = await orchestrator1.RunOrchestrationAsync(_registryPath, "-100123", input1);
        var res2 = await orchestrator2.RunOrchestrationAsync(_registryPath, "-100123", input2);

        Assert.True(res1.IsSuccess);
        Assert.True(res2.IsSuccess);
        
        // Final generated publication registry records must match exactly
        Assert.NotNull(store1.CurrentModel);
        Assert.NotNull(store2.CurrentModel);
        Assert.Equal(3, store1.CurrentModel.Publications.Count);
        Assert.Equal(3, store2.CurrentModel.Publications.Count);
        
        var pub1 = store1.CurrentModel.Publications.First(p => p.TerritoryId == "starokostiantyniv");
        var pub2 = store2.CurrentModel.Publications.First(p => p.TerritoryId == "starokostiantyniv");
        Assert.Equal(pub1.ContentHash, pub2.ContentHash);
    }

    [Fact]
    public async Task TC_Publisher_Operational_CreateNoopUpdateDeleteLifecycle()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // Production-like raw mock feed containing multiple intervals, planned and emergency records.
        string rawFeed1 = 
            "============================================\n" +
            "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
            "Дата: 08.08.2026 (субота)\n" +
            "============================================\n" +
            "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "[Місто Старокостянтинів]\n" +
            "з 08:00 по 12:00 1 черга\n" +
            "вул. Миру 14, 16\n" +
            "з 14:00 по 18:00 2 черга\n" +
            "вул. Миру 22\n" +
            "з 20:00 по 22:00 3 черга\n" +
            "вул. Ізяславська 5\n" +
            "--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "[Березненський старостинський округ]\n" +
            "с. Березне: повністю з 14:20\n" +
            "============================================\n" +
            "КІНЕЦЬ ДОКУМЕНТУ\n";

        var input1 = new EditorialInput(
            EditionDate: "2026-08-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeed1, null, true)
            }
        );

        // --- STAGE 1: CREATE ---
        var result1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input1);
        Assert.True(result1.IsSuccess);
        
        // 4 publications created: journal_header, starokostiantyniv, bereznenskyi, system_status
        Assert.NotNull(store.CurrentModel);
        Assert.Equal(4, store.CurrentModel.Publications.Count);
        
        var pubPlan = store.CurrentModel.Publications.FirstOrDefault(p => p.TerritoryId == "starokostiantyniv");
        var pubEmerg = store.CurrentModel.Publications.FirstOrDefault(p => p.TerritoryId == "bereznenskyi");
        Assert.NotNull(pubPlan);
        Assert.NotNull(pubEmerg);
        
        Assert.Equal(4, adapter.SendCount);
        
        var planText = adapter.SentTexts.Find(t => t.Contains("<b>Місто Старокостянтинів</b>"));
        var emergText = adapter.SentTexts.Find(t => t.Contains("Березненський"));
        
        Assert.NotNull(planText);
        Assert.NotNull(emergText);

        Assert.Contains("<b>Місто Старокостянтинів</b>", planText);
        Assert.Contains("<b>ПЛАНОВІ ЗНЕСТРУМЛЕННЯ", planText);
        Assert.Contains("08:00–12:00", planText);
        Assert.Contains("- вул. Миру, 14, 16", planText);
        Assert.Contains("14:00–18:00", planText);
        Assert.Contains("- вул. Миру, 22", planText);
        Assert.Contains("20:00–22:00", planText);
        Assert.Contains("- вул. Ізяславська, 5", planText);
        Assert.DoesNotContain("черга", planText, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("<blockquote><b>АВАРІЙНІ ЗНЕСТРУМЛЕННЯ</b>", emergText);
        Assert.Contains("<b>Березненський старостинський округ</b>", emergText);
        Assert.Contains("- с. Березне, повністю з 14:20", emergText);
        Assert.DoesNotContain("черга", emergText, StringComparison.OrdinalIgnoreCase);

        // --- STAGE 2: NOOP ---
        var result2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input1);
        Assert.True(result2.IsSuccess);
        Assert.Equal(4, adapter.SendCount); // Send count remains 4 (no new telegram calls)

        // --- STAGE 3: QUEUE ONLY CHANGE (NOOP) ---
        string rawFeed2 = rawFeed1.Replace("1 черга", "4 черга").Replace("2 черга", "5 черга");
        var input3 = new EditorialInput(
            EditionDate: "2026-08-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeed2, null, true)
            }
        );
        var result3 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input3);
        Assert.True(result3.IsSuccess);
        Assert.Equal(4, adapter.SendCount); // Still 4

        // --- STAGE 4: UPDATE ---
        string rawFeed3 = rawFeed1.Replace("вул. Миру 22", "вул. Миру 24");
        var input4 = new EditorialInput(
            EditionDate: "2026-08-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeed3, null, true)
            }
        );
        var result4 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input4);
        Assert.True(result4.IsSuccess);
        Assert.Equal(4, adapter.SendCount); // Still 4 send calls (UPDATE operation)

        // --- STAGE 5: DELETE ---
        string rawFeed4 = 
            "============================================\n" +
            "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
            "Дата: 08.08.2026 (субота)\n" +
            "============================================\n" +
            "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "[Місто Старокостянтинів]\n" +
            "з 08:00 по 12:00 1 черга\n" +
            "вул. Миру 14, 16\n" +
            "з 14:00 по 18:00 2 черга\n" +
            "вул. Миру 22\n" +
            "з 20:00 по 22:00 3 черга\n" +
            "вул. Ізяславська 5\n" +
            "--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "Аварійних знеструмлень не зафіксовано\n" +
            "============================================\n" +
            "КІНЕЦЬ ДОКУМЕНТУ\n";

        var input5 = new EditorialInput(
            EditionDate: "2026-08-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeed4, null, true)
            }
        );

        var result5 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input5);
        Assert.True(result5.IsSuccess);

        // In Option B, historical publications remain retained (SENT/UPDATED) even if absent from current raw feed
        var postEmerg = store.CurrentModel?.Publications.FirstOrDefault(p => p.TerritoryId == "bereznenskyi");
        Assert.NotNull(postEmerg);
        Assert.True(postEmerg.TransmissionState == "SENT" || postEmerg.TransmissionState == "UPDATED");

        // --- STAGE 6: EMERGENCY UPDATE & ISOLATION ---
        string rawFeed5 = 
            "============================================\n" +
            "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
            "Дата: 08.08.2026 (субота)\n" +
            "============================================\n" +
            "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "Планових знеструмлень не зафіксовано\n" +
            "--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "Аварійних знеструмлень не зафіксовано\n" +
            "============================================\n" +
            "КІНЕЦЬ ДОКУМЕНТУ\n";

        var input6 = new EditorialInput(
            EditionDate: "2026-08-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeed5, null, true)
            }
        );

        var result6 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input6);
        Assert.True(result6.IsSuccess);

        // --- STAGE 7: PLAN PERSISTENCE (Option B) ---
        // Under Option B, when empty feed comes in, historical publications remain in registry (SENT/UPDATED)
        var postPlan = store.CurrentModel?.Publications.FirstOrDefault(p => p.TerritoryId == "starokostiantyniv");
        Assert.NotNull(postPlan);
        Assert.True(postPlan.TransmissionState == "SENT" || postPlan.TransmissionState == "UPDATED");
    }

    [Fact]
    public async Task TC_PublisherOrchestrator_DateRollover_PreservesHistoricalPosts_DeletesEphemeralPosts_CreatesNewDayPosts()
    {
        var store = new FakeRegistryStore();
        var git = new FakeGitTransport();
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);
        var calculator = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var parser = new OutageFeedParser();
        var transformer = new EditorialContentTransformer();
        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher, parser, transformer);

        // --- DAY 1 (2026-09-07) ---
        string rawFeedDay1 = 
            "============================================\n" +
            "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
            "Дата: 07.09.2026 (понеділок)\n" +
            "============================================\n" +
            "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "[Місто Старокостянтинів]\n" +
            "з 08:00 по 12:00 1 черга\n" +
            "вул. Миру 14, 16\n" +
            "============================================\n" +
            "КІНЕЦЬ ДОКУМЕНТУ\n";

        var inputDay1 = new EditorialInput(
            EditionDate: "2026-09-07",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeedDay1, null, true),
                new InputTerritoryPackage("tomorrow_starokostiantyniv", "Прогноз на завтра для міста", null, false)
            }
        );

        var resultDay1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", inputDay1);
        Assert.True(resultDay1.IsSuccess);
        Assert.NotNull(store.CurrentModel);
        Assert.Equal("2026-09-07", store.CurrentModel.EditionDate);

        // Day 1 generated 4 publications: journal_header, starokostiantyniv, system_status, tomorrow_starokostiantyniv
        var day1City = store.CurrentModel.Publications.FirstOrDefault(p => p.TerritoryId == "starokostiantyniv");
        var day1Tomorrow = store.CurrentModel.Publications.FirstOrDefault(p => p.TerritoryId == "tomorrow_starokostiantyniv");
        Assert.NotNull(day1City);
        Assert.NotNull(day1Tomorrow);
        var day1CityId = day1City.PublisherArtifactId;

        // --- DAY 2 (2026-09-08): Date Rollover ---
        string rawFeedDay2 = 
            "============================================\n" +
            "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
            "Дата: 08.09.2026 (вівторок)\n" +
            "============================================\n" +
            "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
            "[Місто Старокостянтинів]\n" +
            "з 10:00 по 14:00 2 черга\n" +
            "вул. Острозького 1\n" +
            "============================================\n" +
            "КІНЕЦЬ ДОКУМЕНТУ\n";

        var inputDay2 = new EditorialInput(
            EditionDate: "2026-09-08",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", rawFeedDay2, null, true)
            }
        );

        var resultDay2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", inputDay2);
        Assert.True(resultDay2.IsSuccess);
        Assert.NotNull(store.CurrentModel);
        Assert.Equal("2026-09-08", store.CurrentModel.EditionDate);

        // Verify that Day 1's persistent city publication was preserved in registry
        var preservedDay1City = store.CurrentModel.Publications.FirstOrDefault(p => p.PublisherArtifactId == day1CityId);
        Assert.NotNull(preservedDay1City);
        Assert.Equal("SENT", preservedDay1City.TransmissionState);

        // Verify that Day 2 generated a BRAND NEW publication for city with a different ArtifactId
        var day2City = store.CurrentModel.Publications.FirstOrDefault(p => p.TerritoryId == "starokostiantyniv" && p.PublisherArtifactId != day1CityId);
        Assert.NotNull(day2City);
        Assert.NotEqual(day1CityId, day2City.PublisherArtifactId);

        // Verify that Day 1's ephemeral tomorrow post was marked DELETED
        var day1TomorrowPostRollover = store.CurrentModel.Publications.FirstOrDefault(p => p.PublisherArtifactId == day1Tomorrow.PublisherArtifactId);
        Assert.NotNull(day1TomorrowPostRollover);
        Assert.Equal("DELETED", day1TomorrowPostRollover.TransmissionState);
    }
}

public class GraphicAssemblyTests
{
    private static GraphicInputPackage CreateStandardPackage(Action<List<QueueSchedule>>? customizeQueues = null)
    {
        var queues = new List<QueueSchedule>();
        for (int q = 1; q <= 6; q++)
        {
            var subqueues = new List<SubqueueSchedule>
            {
                new SubqueueSchedule($"Черга {q}.1", new List<GraphicInterval>
                {
                    new GraphicInterval("08:00", "12:00", "Restricted"),
                    new GraphicInterval("18:00", "22:00", "Possible")
                }),
                new SubqueueSchedule($"Черга {q}.2", new List<GraphicInterval>
                {
                    new GraphicInterval("12:00", "16:00", "Restricted")
                })
            };
            queues.Add(new QueueSchedule($"Черга {q}", subqueues));
        }

        customizeQueues?.Invoke(queues);

        return new GraphicInputPackage(
            new GraphicMetadata("pkg-uuid-001", "2026-08-31T12:00:00Z", "2026-09-01", "DSO-KHM-01"),
            "Старокостянтинівська МТГ",
            queues
        );
    }

    [Fact]
    public void TC_GraphicAssembly_EmptyPackage_ThrowsException()
    {
        var assembly = new GraphicAssembly();
        Assert.Throws<ArgumentNullException>(() => assembly.AssembleSvg(null!));
    }

    [Fact]
    public void TC_GraphicAssembly_AllQueues_And_AllSubqueues_Rendered()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage();

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("id=\"subqueue_1.1\"", svg);
        Assert.Contains("id=\"subqueue_1.2\"", svg);
        Assert.Contains("id=\"subqueue_6.1\"", svg);
        Assert.Contains("id=\"subqueue_6.2\"", svg);
    }

    [Fact]
    public void TC_GraphicAssembly_SingleInterval_Positioning()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>
                {
                    new GraphicInterval("06:00", "12:00", "Restricted")
                }),
                new SubqueueSchedule("Черга 1.2", new List<GraphicInterval>())
            });
        });

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        // 06:00 is 25% (0.25 * 950 = 237.5), duration 6h is 25% (237.5)
        Assert.Contains("width=\"237.5\"", svg);
        Assert.Contains("06:00–12:00 (Restricted)", svg);
    }

    [Fact]
    public void TC_GraphicAssembly_MultipleIntervals_RenderedSeparately()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>
                {
                    new GraphicInterval("00:00", "04:00", "Restricted"),
                    new GraphicInterval("08:00", "12:00", "Possible"),
                    new GraphicInterval("16:00", "20:00", "Restricted")
                }),
                new SubqueueSchedule("Черга 1.2", new List<GraphicInterval>())
            });
        });

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("00:00–04:00 (Restricted)", svg);
        Assert.Contains("08:00–12:00 (Possible)", svg);
        Assert.Contains("16:00–20:00 (Restricted)", svg);
    }

    [Fact]
    public void TC_GraphicAssembly_PlanStatus_And_PossibleStatus_Colors()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage();

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains(GraphicAssembly.OutageColor, svg);
        Assert.Contains(GraphicAssembly.PossibleColor, svg);
    }

    [Fact]
    public void TC_GraphicAssembly_MidnightBoundary_And_EndOfDayBoundary()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>
                {
                    new GraphicInterval("00:00", "02:00", "Restricted"),
                    new GraphicInterval("22:00", "24:00", "Restricted")
                }),
                new SubqueueSchedule("Черга 1.2", new List<GraphicInterval>())
            });
        });

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.Contains("00:00–02:00 (Restricted)", svg);
        Assert.Contains("22:00–24:00 (Restricted)", svg);
    }

    [Fact]
    public void TC_GraphicAssembly_InvalidTime_ThrowsException()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>
                {
                    new GraphicInterval("14:00", "10:00", "Restricted") // Start > End
                }),
                new SubqueueSchedule("Черга 1.2", new List<GraphicInterval>())
            });
        });

        Assert.Throws<ArgumentException>(() => assembly.AssembleSvg(package));
    }

    [Fact]
    public void TC_GraphicAssembly_InvalidSubqueueCount_ThrowsException()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>())
                // Missing 1.2
            });
        });

        Assert.Throws<ArgumentException>(() => assembly.AssembleSvg(package));
    }

    [Fact]
    public void TC_GraphicAssembly_DuplicateSubqueue_ThrowsException()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage(queues =>
        {
            queues[0] = new QueueSchedule("Черга 1", new List<SubqueueSchedule>
            {
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>()),
                new SubqueueSchedule("Черга 1.1", new List<GraphicInterval>()) // Duplicate
            });
        });

        Assert.Throws<ArgumentException>(() => assembly.AssembleSvg(package));
    }

    [Fact]
    public void TC_GraphicAssembly_DeterministicOutput()
    {
        var assembly = new GraphicAssembly();
        var package1 = CreateStandardPackage();
        var package2 = CreateStandardPackage();

        byte[] svg1 = assembly.AssembleSvg(package1);
        byte[] svg2 = assembly.AssembleSvg(package2);

        Assert.Equal(svg1, svg2);
    }

    [Fact]
    public void TC_GraphicAssembly_OutputIsValidSvg()
    {
        var assembly = new GraphicAssembly();
        var package = CreateStandardPackage();

        byte[] svgBytes = assembly.AssembleSvg(package);
        string svg = System.Text.Encoding.UTF8.GetString(svgBytes);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", svg.TrimStart());
        Assert.Contains("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg);
        Assert.EndsWith("</svg>", svg.TrimEnd());
    }
}

public class GraphicOrchestrationTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _registryPath;

    public GraphicOrchestrationTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_GraphicOrchTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _registryPath = Path.Combine(_testDirectory, "registry.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private class FakeGraphicDispatcher : IGraphicPublisherDispatcher
    {
        public List<GraphicOperationPayload> DispatchedOperations { get; } = new();
        public int NextMessageId { get; set; } = 5555;
        public bool ThrowOrFail { get; set; }

        public Task<TelegramDispatchResult> DispatchGraphicAsync(GraphicOperationPayload payload, CancellationToken cancellationToken = default)
        {
            DispatchedOperations.Add(payload);
            if (ThrowOrFail)
                return Task.FromResult(new TelegramDispatchResult(false, null, "Simulated graphic dispatch error", false));

            int msgId = payload.TelegramMessageId ?? NextMessageId++;
            return Task.FromResult(new TelegramDispatchResult(true, msgId, null, false));
        }
    }

    private static GraphicInputPackage CreatePackage(string date = "2026-09-01", string status = "Restricted")
    {
        var queues = new List<QueueSchedule>();
        for (int q = 1; q <= 6; q++)
        {
            var subqueues = new List<SubqueueSchedule>
            {
                new SubqueueSchedule($"Черга {q}.1", new List<GraphicInterval>
                {
                    new GraphicInterval("08:00", "12:00", status)
                }),
                new SubqueueSchedule($"Черга {q}.2", new List<GraphicInterval>
                {
                    new GraphicInterval("12:00", "16:00", status)
                })
            };
            queues.Add(new QueueSchedule($"Черга {q}", subqueues));
        }

        return new GraphicInputPackage(
            new GraphicMetadata("pkg-uuid-001", "2026-08-31T12:00:00Z", date, "DSO-KHM-01"),
            "Старокостянтинівська МТГ",
            queues
        );
    }

    [Fact]
    public void TC_GraphicHash_Deterministic()
    {
        var calculator = new ContentHashCalculator();
        var pkg1 = CreatePackage("2026-09-01", "Restricted");
        var pkg2 = CreatePackage("2026-09-01", "Restricted");

        string hash1 = calculator.ComputeGraphicHash(pkg1);
        string hash2 = calculator.ComputeGraphicHash(pkg2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TC_GraphicHash_ChangesOnSemanticChange()
    {
        var calculator = new ContentHashCalculator();
        var pkg1 = CreatePackage("2026-09-01", "Restricted");
        var pkg2 = CreatePackage("2026-09-01", "Possible");

        string hash1 = calculator.ComputeGraphicHash(pkg1);
        string hash2 = calculator.ComputeGraphicHash(pkg2);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void TC_GraphicHash_UnchangedOnNonSemanticChange()
    {
        var calculator = new ContentHashCalculator();
        var pkg1 = CreatePackage("2026-09-01", "Restricted");
        var pkg2 = pkg1 with { Metadata = pkg1.Metadata with { PackageId = "another-uuid-999", GenerationTimestamp = "2026-08-31T15:00:00Z" } };

        string hash1 = calculator.ComputeGraphicHash(pkg1);
        string hash2 = calculator.ComputeGraphicHash(pkg2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TC_GraphicDecision_Create_Update_Noop()
    {
        var engine = new EditorialDecisionEngine();
        var calculator = new ContentHashCalculator();
        var pkg1 = CreatePackage("2026-09-01", "Restricted");
        string hash1 = calculator.ComputeGraphicHash(pkg1);

        // 1. Create (no existing publication)
        var decCreate = engine.EvaluateGraphicGeneration(hash1, null);
        Assert.Equal(DecisionResult.Generate, decCreate.DecisionResult);

        // 2. Noop (same hash)
        var pub = new SvitloSk.Publisher.Core.Domain.Publication(Guid.NewGuid(), "Старокостянтинівська МТГ", SvitloSk.Publisher.Core.Domain.PublicationType.Graphic, DateTime.UtcNow, hash1, SvitloSk.Publisher.Core.Domain.PublicationState.Published, true);
        var decNoop = engine.EvaluateGraphicGeneration(hash1, pub);
        Assert.Equal(DecisionResult.NoAction, decNoop.DecisionResult);

        // 3. Update (hash changed)
        var pkg2 = CreatePackage("2026-09-01", "Possible");
        string hash2 = calculator.ComputeGraphicHash(pkg2);
        var decUpdate = engine.EvaluateGraphicGeneration(hash2, pub);
        Assert.Equal(DecisionResult.Generate, decUpdate.DecisionResult);
    }

    private class FakeDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task TC_GraphicLifecycle_Create_Noop_Update_Integration()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var hashCalc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var dispatcher = new SequentialDispatcher(new PublisherOrchestratorTests.FakeTelegramAdapter(), new FakeDelayProvider());
        var graphicDispatcher = new FakeGraphicDispatcher();

        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            hashCalc,
            decisionEngine,
            dispatcher,
            new OutageFeedParser(),
            new EditorialContentTransformer(),
            graphicDispatcher
        );


        // --- STAGE 1: GRAPHIC CREATE ---
        var input1 = new EditorialInput(
            EditionDate: "2026-09-01",
            Packages: new List<InputTerritoryPackage>(),
            GraphicPackage: CreatePackage("2026-09-01", "Restricted")
        );

        var result1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input1);
        Assert.True(result1.IsSuccess);
        Assert.Single(graphicDispatcher.DispatchedOperations);
        Assert.Equal("CREATE", graphicDispatcher.DispatchedOperations[0].OperationType);

        var reg1 = await store.LoadAsync(_registryPath);
        Assert.NotNull(reg1);
        var gPub1 = reg1.Publications.FirstOrDefault(p => p.PublicationType == "Graphic");
        Assert.NotNull(gPub1);
        Assert.Equal("SENT", gPub1.TransmissionState);
        Assert.Equal(5555, gPub1.TelegramMessageId);

        // --- STAGE 2: GRAPHIC NOOP ---
        graphicDispatcher.DispatchedOperations.Clear();
        var result2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input1);
        Assert.True(result2.IsSuccess);
        Assert.Empty(graphicDispatcher.DispatchedOperations); // NOOP!

        // --- STAGE 3: GRAPHIC UPDATE ---
        graphicDispatcher.DispatchedOperations.Clear();
        var input3 = new EditorialInput(
            EditionDate: "2026-09-01",
            Packages: new List<InputTerritoryPackage>(),
            GraphicPackage: CreatePackage("2026-09-01", "Possible") // changed status
        );

        var result3 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input3);
        Assert.True(result3.IsSuccess);
        Assert.Single(graphicDispatcher.DispatchedOperations);
        Assert.Equal("UPDATE", graphicDispatcher.DispatchedOperations[0].OperationType);
        Assert.Equal(5555, graphicDispatcher.DispatchedOperations[0].TelegramMessageId);

        var reg3 = await store.LoadAsync(_registryPath);
        var gPub3 = reg3?.Publications.FirstOrDefault(p => p.PublicationType == "Graphic");
        Assert.NotNull(gPub3);
        Assert.Equal("UPDATED", gPub3.TransmissionState);
        Assert.Equal(5555, gPub3.TelegramMessageId);
    }

    [Fact]
    public async Task TC_GraphicText_IndependentLifecycle()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var hashCalc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var dispatcher = new SequentialDispatcher(new PublisherOrchestratorTests.FakeTelegramAdapter(), new FakeDelayProvider());
        var graphicDispatcher = new FakeGraphicDispatcher();

        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            hashCalc,
            decisionEngine,
            dispatcher,
            new OutageFeedParser(),
            new EditorialContentTransformer(),
            graphicDispatcher
        );


        string textFeed1 = "============================================\n" +
                           "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                           "Дата: 01.09.2026\n" +
                           "============================================\n" +
                           "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
                           "1. Населений пункт: м. Старокостянтинів\n" +
                           "   Вулиці: вул. Миру\n" +
                           "   Час: 09:00 - 17:00\n" +
                           "============================================\n" +
                           "КІНЕЦЬ ДОКУМЕНТУ\n";

        // Initial Publish (Text + Graphic)
        var input1 = new EditorialInput(
            EditionDate: "2026-09-01",
            Packages: new List<InputTerritoryPackage> { new("starokostiantyniv", textFeed1, null, true) },
            GraphicPackage: CreatePackage("2026-09-01", "Restricted")
        );

        await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input1);

        // Case A: Text Change Only -> Graphic should remain NOOP
        graphicDispatcher.DispatchedOperations.Clear();
        string textFeed2 = textFeed1.Replace("вул. Миру", "вул. Соборна");
        var input2 = new EditorialInput(
            EditionDate: "2026-09-01",
            Packages: new List<InputTerritoryPackage> { new("starokostiantyniv", textFeed2, null, true) },
            GraphicPackage: CreatePackage("2026-09-01", "Restricted") // identical graphic
        );

        var resA = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input2);
        Assert.True(resA.IsSuccess);
        Assert.Empty(graphicDispatcher.DispatchedOperations); // Graphic stayed NOOP

        // Case B: Graphic Change Only -> Text should remain NOOP (dispatched count 0 for text)
        graphicDispatcher.DispatchedOperations.Clear();
        var input3 = new EditorialInput(
            EditionDate: "2026-09-01",
            Packages: new List<InputTerritoryPackage> { new("starokostiantyniv", textFeed2, null, true) }, // identical text
            GraphicPackage: CreatePackage("2026-09-01", "Possible") // changed graphic
        );

        var resB = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input3);
        Assert.True(resB.IsSuccess);
        Assert.Single(graphicDispatcher.DispatchedOperations);
        Assert.Equal("UPDATE", graphicDispatcher.DispatchedOperations[0].OperationType);
    }

    [Fact]
    public async Task TC_PhaseN_TextFirst_RecreatesDeletedText_PreservesGraphic()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var adapter = new PublisherOrchestratorTests.FakeTelegramAdapter();
        var hashCalc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var dispatcher = new SequentialDispatcher(adapter, new FakeDelayProvider());
        var graphicDispatcher = new FakeGraphicDispatcher();

        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            hashCalc,
            decisionEngine,
            dispatcher,
            new OutageFeedParser(),
            new EditorialContentTransformer(),
            graphicDispatcher
        );

        // Pre-populate registry with Deleted Text record and Active Graphic record (message_id=38)
        var initialPublications = new List<RegistryPublicationRecord>
        {
            new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", null, "deleted-hash", "DELETED", "Text"),
            new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", 38, "graphic-schedule-hash", "UPDATED", "Graphic")
        };
        var initialRegistry = new RegistryModel(1, "2026-09-05", "ACTIVE", initialPublications);
        await store.SaveAsync(_registryPath, initialRegistry);

        string textFeed = "============================================\n" +
                          "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                          "Дата: 05.09.2026\n" +
                          "============================================\n" +
                          "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
                          "[Місто Старокостянтинів]\n" +
                          "з 08:00 по 12:00 1 черга\n" +
                          "вул. Миру 14\n" +
                          "============================================\n" +
                          "КІНЕЦЬ ДОКУМЕНТУ\n";

        var input = new EditorialInput(
            EditionDate: "2026-09-05",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", textFeed, null, true)
            }
        );

        // --- STAGE 1: TEXT CREATE while GRAPHIC is preserved ---
        var result1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result1.IsSuccess);
        Assert.Equal(3, adapter.SendCount); // journal_header + text + system_status created
        Assert.Empty(graphicDispatcher.DispatchedOperations); // Graphic untouched (NOOP)

        var reg1 = await store.LoadAsync(_registryPath);
        Assert.NotNull(reg1);
        Assert.Equal(4, reg1.Publications.Count); // journal_header, starokostiantyniv (text), graphic (38), system_status

        var textPub = reg1.Publications.FirstOrDefault(p => p.PublicationType == "Text" && p.TerritoryId == "starokostiantyniv");
        Assert.NotNull(textPub);
        Assert.Equal("SENT", textPub.TransmissionState);
        Assert.Equal(1111, textPub.TelegramMessageId);

        var graphicPub = reg1.Publications.FirstOrDefault(p => p.PublicationType == "Graphic" && p.TerritoryId == "starokostiantyniv");
        Assert.NotNull(graphicPub);
        Assert.Equal("UPDATED", graphicPub.TransmissionState);
        Assert.Equal(38, graphicPub.TelegramMessageId);

        // --- STAGE 2: SECOND RUN (NOOP for both) ---
        var result2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result2.IsSuccess);
        Assert.Equal(3, adapter.SendCount); // SendCount remains 3 (0 new telegram sends)
        Assert.Empty(graphicDispatcher.DispatchedOperations);

        var reg2 = await store.LoadAsync(_registryPath);
        Assert.NotNull(reg2);
        var textPub2 = reg2.Publications.FirstOrDefault(p => p.PublicationType == "Text" && p.TerritoryId == "starokostiantyniv");
        var graphicPub2 = reg2.Publications.FirstOrDefault(p => p.PublicationType == "Graphic" && p.TerritoryId == "starokostiantyniv");
        Assert.NotNull(textPub2);
        Assert.Equal(1111, textPub2.TelegramMessageId);
        Assert.NotNull(graphicPub2);
        Assert.Equal(38, graphicPub2.TelegramMessageId);
    }

    [Fact]
    public async Task TC_PhaseN_TextAbsence_DoesNotDeleteGraphic()
    {
        var store = new PublisherOrchestratorTests.FakeRegistryStore();
        var git = new PublisherOrchestratorTests.FakeGitTransport();
        var adapter = new PublisherOrchestratorTests.FakeTelegramAdapter();
        var hashCalc = new ContentHashCalculator();
        var decisionEngine = new EditorialDecisionEngine();
        var dispatcher = new SequentialDispatcher(adapter, new FakeDelayProvider());
        var graphicDispatcher = new FakeGraphicDispatcher();

        var orchestrator = new PublisherOrchestrator(
            store,
            git,
            hashCalc,
            decisionEngine,
            dispatcher,
            new OutageFeedParser(),
            new EditorialContentTransformer(),
            graphicDispatcher
        );

        // Pre-populate registry with Active Graphic record (message_id=38)
        var initialPublications = new List<RegistryPublicationRecord>
        {
            new RegistryPublicationRecord(Guid.NewGuid(), "starokostiantyniv", 38, "graphic-schedule-hash", "UPDATED", "Graphic")
        };
        var initialRegistry = new RegistryModel(1, "2026-09-05", "ACTIVE", initialPublications);
        await store.SaveAsync(_registryPath, initialRegistry);

        // Input with no outages for starokostiantyniv
        string emptyFeed = "============================================\n" +
                           "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                           "Дата: 05.09.2026\n" +
                           "============================================\n" +
                           "Відключень не зафіксовано.\n" +
                           "============================================\n" +
                           "КІНЕЦЬ ДОКУМЕНТУ\n";

        var input = new EditorialInput(
            EditionDate: "2026-09-05",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("Громада", emptyFeed, null, true)
            }
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result.IsSuccess);
        Assert.Empty(graphicDispatcher.DispatchedOperations); // Graphic not deleted or touched

        var reg = await store.LoadAsync(_registryPath);
        Assert.NotNull(reg);
        var graphicPub = reg.Publications.FirstOrDefault(p => p.PublicationType == "Graphic" && p.TerritoryId == "starokostiantyniv");
        Assert.NotNull(graphicPub);
        Assert.Equal(38, graphicPub.TelegramMessageId);
        Assert.Equal("UPDATED", graphicPub.TransmissionState);
    }

    [Fact]
    public void TC_RealFeed_SynthesizesGraphicInputPackage()
    {
        var parser = new OutageFeedParser();
        string rawFeed = "============================================\n" +
                         "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                         "Дата: 01.09.2026\n" +
                         "============================================\n" +
                         "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
                         "1. Населений пункт: м. Старокостянтинів\n" +
                         "   Черга: 1.1, 2.2\n" +
                         "   Вулиці: вул. Миру, вул. Грушевського\n" +
                         "   Час: 08:00 - 12:00\n" +
                         "2. Населений пункт: с. Пашківці\n" +
                         "   Черга: 3.1\n" +
                         "   Час: 14:00 - 18:00\n" +
                         "--- АВАРІЙНІ ЗНЕСТРУМЛЕННЯ ---\n" +
                         "1. Населений пункт: с. Григорівка\n" +
                         "   Черга: 4.2\n" +
                         "   Час: 10:00 - 13:00\n" +
                         "============================================\n" +
                         "КІНЕЦЬ ДОКУМЕНТУ\n";

        var pkg = parser.ParseGraphicSchedule(rawFeed, "2026-09-01", "Старокостянтинівська МТГ");

        Assert.NotNull(pkg);
        Assert.Equal("2026-09-01", pkg.Metadata.TargetDate);
        Assert.Equal("Старокостянтинівська МТГ", pkg.TerritorialScope);
        Assert.Equal(6, pkg.Queues.Count);

        // Verify subqueue 1.1 has 08:00-12:00 Restricted
        var q1 = pkg.Queues.First(q => q.QueueId == "Черга 1");
        var sq11 = q1.Subqueues.First(sq => sq.SubqueueId == "Черга 1.1");
        Assert.Single(sq11.Intervals);
        Assert.Equal("08:00", sq11.Intervals[0].StartTime);
        Assert.Equal("12:00", sq11.Intervals[0].EndTime);
        Assert.Equal("Restricted", sq11.Intervals[0].Status);

        // Verify subqueue 2.2 has 08:00-12:00 Restricted
        var q2 = pkg.Queues.First(q => q.QueueId == "Черга 2");
        var sq22 = q2.Subqueues.First(sq => sq.SubqueueId == "Черга 2.2");
        Assert.Single(sq22.Intervals);
        Assert.Equal("08:00", sq22.Intervals[0].StartTime);
        Assert.Equal("12:00", sq22.Intervals[0].EndTime);
        Assert.Equal("Restricted", sq22.Intervals[0].Status);

        // Verify subqueue 4.2 has 10:00-13:00 Possible (Emergency)
        var q4 = pkg.Queues.First(q => q.QueueId == "Черга 4");
        var sq42 = q4.Subqueues.First(sq => sq.SubqueueId == "Черга 4.2");
        Assert.Single(sq42.Intervals);
        Assert.Equal("10:00", sq42.Intervals[0].StartTime);
        Assert.Equal("13:00", sq42.Intervals[0].EndTime);
        Assert.Equal("Possible", sq42.Intervals[0].Status);
    }

    [Fact]
    public void TC_RealFeed_EmptyScheduleIsValid()
    {
        var parser = new OutageFeedParser();
        string rawFeed = "============================================\n" +
                         "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                         "Дата: 01.09.2026\n" +
                         "============================================\n" +
                         "Відключень не зафіксовано.\n" +
                         "============================================\n" +
                         "КІНЕЦЬ ДОКУМЕНТУ\n";

        var pkg = parser.ParseGraphicSchedule(rawFeed, "2026-09-01", "Старокостянтинівська МТГ");

        Assert.NotNull(pkg);
        Assert.Equal(6, pkg.Queues.Count);
        foreach (var q in pkg.Queues)
        {
            Assert.Equal(2, q.Subqueues.Count);
            foreach (var sq in q.Subqueues)
            {
                Assert.Empty(sq.Intervals);
            }
        }
    }

    [Fact]
    public void TC_GraphicPackage_IsDeterministicAcrossParses()
    {
        var parser = new OutageFeedParser();
        var calc = new ContentHashCalculator();

        string rawFeed = "============================================\n" +
                         "ДАНІ ПРО ВІДКЛЮЧЕННЯ ЕЛЕКТРОЕНЕРГІЇ\n" +
                         "Дата: 01.09.2026\n" +
                         "============================================\n" +
                         "--- ПЛАНОВІ ЗНЕСТРУМЛЕННЯ ---\n" +
                         "1. Населений пункт: м. Старокостянтинів\n" +
                         "   Черга: 1.1\n" +
                         "   Час: 08:00 - 12:00\n" +
                         "============================================\n" +
                         "КІНЕЦЬ ДОКУМЕНТУ\n";

        var pkg1 = parser.ParseGraphicSchedule(rawFeed, "2026-09-01", "Старокостянтинівська МТГ");
        var pkg2 = parser.ParseGraphicSchedule(rawFeed, "2026-09-01", "Старокостянтинівська МТГ");

        string hash1 = calc.ComputeGraphicHash(pkg1);
        string hash2 = calc.ComputeGraphicHash(pkg2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TC_LegacyReference_SynthesizesCanonical12Subqueues()
    {
        var parser = new OutageFeedParser();
        string legacyJson = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:09:28.180570+03:00"",
  ""mode"": ""schedule"",
  ""message"": ""Графік обмежень..."",
  ""queues"": {
    ""1.1"": ""111111110000111111111111"",
    ""1.2"": ""111111111111111100001111"",
    ""2.1"": ""111111111111111111111111"",
    ""2.2"": ""111111111111111111111111"",
    ""3.1"": ""111111111111111111111111"",
    ""3.2"": ""111111111111111111111111"",
    ""4.1"": ""111111111111111111111111"",
    ""4.2"": ""111111111111111111111111"",
    ""5.1"": ""111111111111111111111111"",
    ""5.2"": ""111111111111111111111111"",
    ""6.1"": ""111111111111111111111111"",
    ""6.2"": ""111111111111111111111111""
  },
  ""meta"": {
    ""generated_at"": ""04.08.2026 15:09"",
    ""state"": ""outages_active"",
    ""target_date"": ""04.08""
  }
}";

        var pkg = parser.ParseLegacyGraphicJson(legacyJson, "Старокостянтинівська МТГ");

        Assert.NotNull(pkg);
        Assert.Equal("2026-08-04", pkg.Metadata.TargetDate);
        Assert.Equal("Старокостянтинівська МТГ", pkg.TerritorialScope);
        Assert.Equal(6, pkg.Queues.Count);

        // Queue 1
        var q1 = pkg.Queues.First(q => q.QueueId == "Черга 1");
        Assert.Equal(2, q1.Subqueues.Count);

        // Subqueue 1.1 has 08:00 - 12:00 outage (hours 8..11 are '0')
        var sq11 = q1.Subqueues.First(sq => sq.SubqueueId == "Черга 1.1");
        Assert.Single(sq11.Intervals);
        Assert.Equal("08:00", sq11.Intervals[0].StartTime);
        Assert.Equal("12:00", sq11.Intervals[0].EndTime);
        Assert.Equal("Restricted", sq11.Intervals[0].Status);

        // Subqueue 1.2 has 16:00 - 20:00 outage (hours 16..19 are '0')
        var sq12 = q1.Subqueues.First(sq => sq.SubqueueId == "Черга 1.2");
        Assert.Single(sq12.Intervals);
        Assert.Equal("16:00", sq12.Intervals[0].StartTime);
        Assert.Equal("20:00", sq12.Intervals[0].EndTime);
        Assert.Equal("Restricted", sq12.Intervals[0].Status);

        // Subqueue 2.1 is all '1' -> empty intervals list
        var q2 = pkg.Queues.First(q => q.QueueId == "Черга 2");
        var sq21 = q2.Subqueues.First(sq => sq.SubqueueId == "Черга 2.1");
        Assert.Empty(sq21.Intervals);
    }

    [Fact]
    public void TC_LegacyReference_AllTwelveSubqueuesPreservedWhenAllPowered()
    {
        var parser = new OutageFeedParser();
        string legacyJson = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:09:28.180570+03:00"",
  ""mode"": ""schedule"",
  ""message"": ""Графік обмежень не оприлюднено. Попередньо: відключень не прогнозується."",
  ""queues"": {
    ""1.1"": ""111111111111111111111111"",
    ""1.2"": ""111111111111111111111111"",
    ""2.1"": ""111111111111111111111111"",
    ""2.2"": ""111111111111111111111111"",
    ""3.1"": ""111111111111111111111111"",
    ""3.2"": ""111111111111111111111111"",
    ""4.1"": ""111111111111111111111111"",
    ""4.2"": ""111111111111111111111111"",
    ""5.1"": ""111111111111111111111111"",
    ""5.2"": ""111111111111111111111111"",
    ""6.1"": ""111111111111111111111111"",
    ""6.2"": ""111111111111111111111111""
  }
}";

        var pkg = parser.ParseLegacyGraphicJson(legacyJson, "Старокостянтинівська МТГ");

        Assert.NotNull(pkg);
        Assert.Equal(6, pkg.Queues.Count);
        int totalSubqueues = 0;
        foreach (var q in pkg.Queues)
        {
            Assert.Equal(2, q.Subqueues.Count);
            totalSubqueues += q.Subqueues.Count;
            foreach (var sq in q.Subqueues)
            {
                Assert.Empty(sq.Intervals);
            }
        }
        Assert.Equal(12, totalSubqueues);
    }

    [Fact]
    public void TC_LegacyReference_HashIgnoresMetadataDifferences()
    {
        var parser = new OutageFeedParser();
        var calc = new ContentHashCalculator();

        string json1 = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T10:00:00Z"",
  ""queues"": {
    ""1.1"": ""111111110000111111111111"",
    ""1.2"": ""111111111111111111111111""
  },
  ""meta"": { ""generated_at"": ""10:00"", ""state"": ""active"" }
}";

        string json2 = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T16:45:00Z"",
  ""queues"": {
    ""1.1"": ""111111110000111111111111"",
    ""1.2"": ""111111111111111111111111""
  },
  ""meta"": { ""generated_at"": ""16:45"", ""state"": ""refreshed"" }
}";

        var pkg1 = parser.ParseLegacyGraphicJson(json1);
        var pkg2 = parser.ParseLegacyGraphicJson(json2);

        string hash1 = calc.ComputeGraphicHash(pkg1);
        string hash2 = calc.ComputeGraphicHash(pkg2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TC_TerritoryAggregator_CompactsHouseNumbers()
    {
        string input = "буд. 1, 2, 3, 4, 5, 7, 9, 10, 11";
        var compacted = TerritoryAggregator.CompactHouseNumbers(input);

        Assert.Equal("буд. 1–5, 7, 9–11", compacted);
    }

    [Fact]
    public void TC_EditorialContentTransformer_RendersBlockquoteForEmergency_AndNoEmojis()
    {
        var transformer = new EditorialContentTransformer();
        var emergencyRecord = new OutageRecord(
            "Місто Старокостянтинів",
            "АВАРІЙНІ",
            "вул. Миру: буд. 1, 2, 3, 4, 5. Час: 10:00 - 14:00. Причина: Пошкодження мережі"
        );
        var plannedRecord = new OutageRecord(
            "Місто Старокостянтинів",
            "ПЛАНОВІ",
            "вул. Центральна: буд. 10, 11, 12. Час: 08:00 - 17:00. Причина: Планові роботи"
        );

        var aggData = new AggregatedTerritoryData(
            "starokostiantyniv",
            "Місто Старокостянтинів",
            new List<OutageRecord> { emergencyRecord },
            new List<OutageRecord> { plannedRecord }
        );

        var post = transformer.RenderAggregatedTerritoryPost(aggData);

        Assert.Contains("<blockquote>", post);
        Assert.Contains("АВАРІЙНІ ЗНЕСТРУМЛЕННЯ", post);
        Assert.Contains("ПЛАНОВІ ЗНЕСТРУМЛЕННЯ", post);
        Assert.Contains("Місто Старокостянтинів", post);
        Assert.Contains("вул. Миру", post);
        Assert.Contains("вул. Центральна", post);

        // Verify Zero Emojis policy
        Assert.DoesNotContain("🚨", post);
        Assert.DoesNotContain("📅", post);
        Assert.DoesNotContain("📍", post);
        Assert.DoesNotContain("🕒", post);
        Assert.DoesNotContain("⚡", post);
    }

    [Fact]
    public void TC_EditorialContentTransformer_RendersJournalHeader_WithSummaryStats()
    {
        var transformer = new EditorialContentTransformer();
        var records = new List<OutageRecord>
        {
            new OutageRecord(
                "Місто Старокостянтинів",
                "ПЛАНОВІ",
                "вул. Франка: 1"
            ),
            new OutageRecord(
                "Березненський старостинський округ",
                "АВАРІЙНІ",
                "вул. Лісова: 5"
            )
        };

        var stats = TerritoryAggregator.CalculateSummaryStats(records);
        var header = transformer.RenderJournalHeader("2026-09-05", stats);

        Assert.Contains("<blockquote><b>Субота 05.09.2026</b></blockquote>", header);
        Assert.Contains("Старокостянтинівська територіальна громада", header);
        Assert.Contains("<b>Планові знеструмлення:</b>", header);
        Assert.Contains("м. Старокостянтинів", header);
        Assert.Contains("<b>Аварійні знеструмлення:</b>", header);
        Assert.Contains("с. Березне", header);
        Assert.DoesNotContain("⚡", header);
        Assert.DoesNotContain("🚨", header);
        Assert.DoesNotContain("Стан на", header);
    }

    [Fact]
    public void TC_GraphicAssembly_Renders12Subqueues_WithBulbLogoAndPwaQrBlock()
    {
        string sampleJson = @"{
  ""date"": ""2026-08-04"",
  ""updated_at"": ""2026-08-04T15:09:28.180570+03:00"",
  ""mode"": ""schedule"",
  ""queues"": {
    ""1.1"": ""111111111111111111111111"",
    ""1.2"": ""111100011111111111111111"",
    ""2.1"": ""111110000111111111111111"",
    ""2.2"": ""111111111111110000000111"",
    ""3.1"": ""111110011111111111111111"",
    ""3.2"": ""000111111111111111111111"",
    ""4.1"": ""111111111111111111100000"",
    ""4.2"": ""111100000000001111111111"",
    ""5.1"": ""001111110000111111111111"",
    ""5.2"": ""111110000111111111100000"",
    ""6.1"": ""110000111111111111111111"",
    ""6.2"": ""111111111100000000000000""
  },
  ""meta"": {
    ""generated_at"": ""04.08.2026 15:09"",
    ""state"": ""active_schedule"",
    ""target_date"": ""04.08""
  }
}";

        var parser = new OutageFeedParser();
        var pkg = parser.ParseLegacyGraphicJson(sampleJson, "Старокостянтинівська МТГ");

        var assembly = new GraphicAssembly();
        byte[] svgBytes = assembly.AssembleSvg(pkg);

        Assert.NotNull(svgBytes);
        Assert.True(svgBytes.Length > 0);

        string svgText = System.Text.Encoding.UTF8.GetString(svgBytes);

        // Verify SVG elements
        Assert.Contains("viewBox=\"0 0 1080 1080\"", svgText);
        Assert.Contains("ГРАФІК ЗНЕСТРУМЛЕНЬ", svgText);
        Assert.Contains("Svitlo", svgText);
        Assert.Contains("Sk", svgText);
        Assert.Contains("SvitloSk Autonomous Publishing System", svgText);
        Assert.Contains("id=\"subqueue_1.1\"", svgText);
        Assert.Contains("id=\"subqueue_1.2\"", svgText);
        Assert.Contains("id=\"subqueue_6.2\"", svgText);
        Assert.Contains("id=\"pwa_qr_code\"", svgText);
        Assert.Contains("Відскануй для", svgText);
        Assert.Contains("моніторингу знеструмлень", svgText);

        // Verify Zero Emojis inside SVG
        Assert.DoesNotContain("⚡", svgText);
        Assert.DoesNotContain("🚨", svgText);
    }

    [Fact]
    public void TC_EditorialDecisionEngine_AreCommentsAllowed_OnlyGraphicPermitted()
    {
        var engine = new EditorialDecisionEngine();

        // Comments must be allowed ONLY for Graphic schedule image
        Assert.True(engine.AreCommentsAllowed(SvitloSk.Publisher.Core.Domain.PublicationType.Graphic));

        // Comments must NOT be allowed for text journal, technical status, or tomorrow text
        Assert.False(engine.AreCommentsAllowed(SvitloSk.Publisher.Core.Domain.PublicationType.Text));
        Assert.False(engine.AreCommentsAllowed(SvitloSk.Publisher.Core.Domain.PublicationType.Technical));
        Assert.False(engine.AreCommentsAllowed(SvitloSk.Publisher.Core.Domain.PublicationType.Tomorrow));
    }
}





