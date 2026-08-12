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

    private class FakeRegistryStore : IRegistryStore
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

    private class FakeGitTransport : IGitTransport
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

    private class FakeTelegramAdapter : ITelegramAdapter
    {
        public int SendCount { get; private set; }
        public bool ForceFatal { get; set; }

        public Task<TelegramDispatchResult> SendAsync(string chatNameOrId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            SendCount++;
            if (ForceFatal)
            {
                return Task.FromResult(new TelegramDispatchResult(false, null, "Unauthorized", false));
            }
            return Task.FromResult(new TelegramDispatchResult(true, 1111, null, false));
        }

        public Task<TelegramDispatchResult> UpdateAsync(string chatNameOrId, int messageId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramDispatchResult(true, messageId, null, false));
        }

        public Task<TelegramDispatchResult> DeleteAsync(string chatNameOrId, int messageId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
        }
    }

    private class FakeDelayProvider : IDelayProvider
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => Task.CompletedTask;
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

        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("svitlovodsk", "Text content", null, true)
            }
        );

        var result = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, adapter.SendCount);
        Assert.NotNull(store.CurrentModel);
        Assert.Single(store.CurrentModel.Publications);
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

        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("svitlovodsk", "Text content", null, true)
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

        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher);

        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage>
            {
                new InputTerritoryPackage("svitlovodsk", "Text content", null, true)
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

        var orchestrator = new PublisherOrchestrator(store, git, calculator, decisionEngine, dispatcher);

        // Run 1: Create a publication in the registry. 
        // We simulate that this registry represents a publication that was already processed in a prior run
        // so that the next run (Run 2) with the same input detects it and makes a NO_ACTION (Keep) decision.
        var pkg = new InputTerritoryPackage("svitlovodsk", "Text content", null, true);
        var input = new EditorialInput(
            EditionDate: "2026-08-12",
            Packages: new List<InputTerritoryPackage> { pkg }
        );

        // Run 1 completes, generating the registry
        var result1 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result1.IsSuccess);
        Assert.Equal(1, adapter.SendCount);
        Assert.NotNull(store.CurrentModel);

        // Run 2: Re-run with the exact same input. 
        // The orchestrator loads the registry, matches the existing publication hash,
        // and the decision engine should produce Valid/Keep decisions (resulting in NO additional Send calls).
        var result2 = await orchestrator.RunOrchestrationAsync(_registryPath, "-100123", input);
        Assert.True(result2.IsSuccess);
        Assert.Equal(1, adapter.SendCount); // SendCount remains 1 (no duplicate sent)
    }
}
