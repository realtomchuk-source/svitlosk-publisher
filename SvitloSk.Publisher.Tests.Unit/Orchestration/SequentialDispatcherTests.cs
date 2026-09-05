using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Orchestration;
using SvitloSk.Publisher.Core.Engine;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Orchestration;

public class SequentialDispatcherTests
{
    private class FakeDelayProvider : IDelayProvider
    {
        public List<int> DelaysCalled { get; } = new();

        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DelaysCalled.Add(milliseconds);
            return Task.CompletedTask;
        }
    }

    private class FakeTelegramAdapter : ITelegramAdapter
    {
        public List<string> CallSequence { get; } = new();
        public Func<string, string, byte[]?, CancellationToken, Task<TelegramDispatchResult>>? OnSend { get; set; }
        public Func<string, int, string, byte[]?, CancellationToken, Task<TelegramDispatchResult>>? OnUpdate { get; set; }
        public Func<string, int, CancellationToken, Task<TelegramDispatchResult>>? OnDelete { get; set; }

        public Task<TelegramDispatchResult> SendAsync(string chatNameOrId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            CallSequence.Add($"Send:{chatNameOrId}");
            if (OnSend != null) return OnSend(chatNameOrId, text, graphicBytes, cancellationToken);
            return Task.FromResult(new TelegramDispatchResult(true, 12345, null, false));
        }

        public Task<TelegramDispatchResult> UpdateAsync(string chatNameOrId, int messageId, string text, byte[]? graphicBytes = null, CancellationToken cancellationToken = default)
        {
            CallSequence.Add($"Update:{messageId}");
            if (OnUpdate != null) return OnUpdate(chatNameOrId, messageId, text, graphicBytes, cancellationToken);
            return Task.FromResult(new TelegramDispatchResult(true, messageId, null, false));
        }

        public Task<TelegramDispatchResult> DeleteAsync(string chatNameOrId, int messageId, CancellationToken cancellationToken = default)
        {
            CallSequence.Add($"Delete:{messageId}");
            if (OnDelete != null) return OnDelete(chatNameOrId, messageId, cancellationToken);
            return Task.FromResult(new TelegramDispatchResult(true, null, null, false));
        }
    }

    [Fact]
    public async Task E01_T01_EmptyDecisions_ShouldCompleteSuccessfully()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var result = await dispatcher.DispatchAsync("-100123", Array.Empty<EditorialDecision>());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Empty(adapter.CallSequence);
    }

    [Fact]
    public async Task E01_T02_OneCreateOperation_ShouldCallSendOnce()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var decisions = new[] { new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "hash1") };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Single(adapter.CallSequence);
        Assert.Equal("Send:-100123", adapter.CallSequence[0]);
    }

    [Fact]
    public async Task E01_T03_UpdateOperation_ShouldInvokeUpdate()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var decisions = new[] { new EditorialDecision(DecisionResult.Update, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "hash1", 9999) };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Single(adapter.CallSequence);
        Assert.Equal("Update:9999", adapter.CallSequence[0]);
    }

    [Fact]
    public async Task E01_T04_DeleteOperation_ShouldInvokeDelete()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var decisions = new[] { new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, Guid.NewGuid(), "staro", null, 9999) };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Single(adapter.CallSequence);
        Assert.Equal("Delete:9999", adapter.CallSequence[0]);
    }

    [Fact]
    public async Task E01_T05_KeepAndNoAction_ShouldNotCallTelegramApi()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var decisions = new[] 
        { 
            new EditorialDecision(DecisionResult.Keep, PublicationClassification.Persistent),
            new EditorialDecision(DecisionResult.NoAction, PublicationClassification.Ephemeral)
        };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.TotalProcessed);
        Assert.Empty(adapter.CallSequence);
    }

    [Fact]
    public async Task E01_T06_SequenceOfOperations_ShouldBeSequentialWithThrottling()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        var decisions = new[]
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1"),
            new EditorialDecision(DecisionResult.Update, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h2", 9999),
            new EditorialDecision(DecisionResult.Delete, PublicationClassification.Ephemeral, Guid.NewGuid(), "staro", null, 9999)
        };

        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.TotalProcessed);
        Assert.Equal(3, adapter.CallSequence.Count);
        Assert.Equal("Send:-100123", adapter.CallSequence[0]);
        Assert.Equal("Update:9999", adapter.CallSequence[1]);
        Assert.Equal("Delete:9999", adapter.CallSequence[2]);

        // Throttling delays should have occurred twice (between 1-2 and 2-3)
        Assert.Equal(2, delay.DelaysCalled.Count);
        Assert.All(delay.DelaysCalled, d => Assert.Equal(1000, d));
    }

    [Fact]
    public async Task E01_T07_429WithRetryAfter_ShouldRetryAndProceedOnSuccess()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        int attempts = 0;
        adapter.OnSend = (c, t, g, token) =>
        {
            attempts++;
            if (attempts == 1)
            {
                return Task.FromResult(new TelegramDispatchResult(false, null, "Rate limit exceeded", true, 5));
            }
            return Task.FromResult(new TelegramDispatchResult(true, 123, null, false));
        };

        var decisions = new[] { new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1") };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Equal(2, adapter.CallSequence.Count); // 1 fail, 1 success retry
        Assert.Single(delay.DelaysCalled);
        Assert.Equal(5000, delay.DelaysCalled[0]);
    }

    [Fact]
    public async Task E01_T08_429ExhaustingMaxAttempts_ShouldAbortRun()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        adapter.OnSend = (c, t, g, token) => 
            Task.FromResult(new TelegramDispatchResult(false, null, "Rate limit exceeded", true, 5));

        var decisions = new[]
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1"),
            new EditorialDecision(DecisionResult.Update, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h2")
        };

        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed); // Stopped on first decision
        Assert.Equal(3, adapter.CallSequence.Count); // Attempt 1, 2, 3
        Assert.Equal(2, delay.DelaysCalled.Count); // 2 delays between 3 attempts
    }

    [Fact]
    public async Task E01_T09_Transient5xx_ShouldExponentialBackoffAndSucceed()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        int attempts = 0;
        adapter.OnSend = (c, t, g, token) =>
        {
            attempts++;
            if (attempts < 3)
            {
                return Task.FromResult(new TelegramDispatchResult(false, null, "Gateway Timeout", true));
            }
            return Task.FromResult(new TelegramDispatchResult(true, 123, null, false));
        };

        var decisions = new[] { new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1") };
        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, adapter.CallSequence.Count);
        Assert.Equal(2, delay.DelaysCalled.Count);
        Assert.Equal(1000, delay.DelaysCalled[0]);
        Assert.Equal(2000, delay.DelaysCalled[1]);
    }

    [Fact]
    public async Task E01_T10_Fatal401_ShouldAbortImmediately()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        adapter.OnSend = (c, t, g, token) =>
            Task.FromResult(new TelegramDispatchResult(false, null, "Unauthorized", false));

        var decisions = new[]
        {
            new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1"),
            new EditorialDecision(DecisionResult.Update, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h2")
        };

        var result = await dispatcher.DispatchAsync("-100123", decisions);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.TotalProcessed);
        Assert.Single(adapter.CallSequence); // Aborted without retrying
        Assert.Empty(delay.DelaysCalled);
    }

    [Fact]
    public async Task E01_T11_CancellationBeforeOperation_ShouldThrow()
    {
        var adapter = new FakeTelegramAdapter();
        var delay = new FakeDelayProvider();
        var dispatcher = new SequentialDispatcher(adapter, delay);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var decisions = new[] { new EditorialDecision(DecisionResult.Create, PublicationClassification.Persistent, Guid.NewGuid(), "staro", "h1") };
        
        await Assert.ThrowsAsync<OperationCanceledException>(() => dispatcher.DispatchAsync("-100123", decisions, cts.Token));
    }
}
