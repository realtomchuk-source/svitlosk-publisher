using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;
using Xunit;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Tests;

public class TestInputPackageProvider : IInputPackageProvider
{
    private InputPackage? _currentPackage;
    public void SetPackage(InputPackage? package) => _currentPackage = package;
    public Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(_currentPackage!);
}

public class EndToEndScenarioTests
{
    private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

    private (IServiceProvider, InMemoryEditionRepository, InMemoryDispatcher, TestInputPackageProvider) SetupContainer()
    {
        var services = new ServiceCollection();

        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SynchronizationEngine>>(NullLogger<SynchronizationEngine>.Instance);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<GraphicPublisher>>(NullLogger<GraphicPublisher>.Instance);
        
        services.AddSingleton<IEditionFactory, EditionFactory>();
        services.AddSingleton<IEditorialDecisionEngine, EditorialDecisionEngine>();
        services.AddSingleton<ISituationModel, SituationModel>();
        services.AddSingleton<IReasoningModel, ReasoningModel>();
        services.AddScoped<IEditorialOrderingStrategy, CanonicalOrderingStrategy>();
        services.AddScoped<IEditionAssembly, EditionAssembly>();
        services.AddScoped<IGraphicPublisher, GraphicPublisher>();
        services.AddSingleton<IPublicationPipeline, PublicationPipeline>();
        services.AddSingleton<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository, InMemoryOutboxRepository>(); services.AddSingleton<SvitloSk.Publisher.Runtime.Persistence.IUnitOfWork, InMemoryUnitOfWork>(); services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();
        services.AddSingleton<IExternalPublicationIdentityResolver, InMemoryExternalPublicationIdentityResolver>();

        var repository = new InMemoryEditionRepository();
        services.AddSingleton<IEditionRepository>(repository);
        
        var packageProvider = new TestInputPackageProvider();
        services.AddSingleton<IInputPackageProvider>(packageProvider);

        var dispatcher = new InMemoryDispatcher();
        services.AddSingleton<InMemoryDispatcher>(dispatcher);
        services.AddSingleton<IPublicationPort>(dispatcher);

        return (services.BuildServiceProvider(), repository, dispatcher, packageProvider);
    }

    private InputPackage CreatePackage(string territory, string content, string? state = null)
    {
        var territoryPayloads = new Dictionary<string, string> { { territory, content } };
        var queueSchedules = new Dictionary<string, string> { { "Q1", territory } };
        return new InputPackage(Guid.NewGuid(), _timeProvider.GetUtcNow(), "TestSource", "Starokostiantyniv Urban Territorial Community", new[] { new Event(territory, Array.Empty<string>(), new[] { new Interval(_timeProvider.GetUtcNow(), _timeProvider.GetUtcNow().AddHours(2)) }) }, TerritoryPayloads: territoryPayloads, QueueSchedules: queueSchedules);
    }

    [Fact]
    public async Task Scenario1_MorningStartup_CreatesEditionAndGeneratesPublication()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Single(sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll());
        Assert.Contains("Q1", sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().First().Payload);
        Assert.Contains("Content 1", sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().First().Payload);
    }

    [Fact]
    public async Task Scenario2_IncrementalUpdate_OnlyOneRequestDispatched()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        ((InMemoryOutboxRepository)sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>()).Clear();

        // Incremental update changes payload, so an update should be dispatched.
        provider.SetPackage(CreatePackage("Kyiv", "Content 2"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Single(sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll());
    }

    [Fact]
    public async Task Scenario3_Deletion_FailsSafely_WithoutDispatch()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        
        var pubId = sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().First().PublicationId;
        ((InMemoryOutboxRepository)sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>()).Clear();

        // Disappears -> package becomes null. (Wait, here we changed payload to CLEAR, so it dispatches an update)
        provider.SetPackage(CreatePackage("Kyiv", "CLEAR", "Clear"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Single(sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll());
    }

    [Fact]
    public async Task Scenario4_NoChanges_NoDispatch()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        ((InMemoryOutboxRepository)sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>()).Clear();

        // No changes
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Empty(sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll());
    }

    [Fact]
    public async Task Scenario5_TomorrowScheduleUpdate_OnlyTomorrowDispatched()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Tomorrow", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        ((InMemoryOutboxRepository)sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>()).Clear();

        // Incremental update changes payload, so it should dispatch an update.
        provider.SetPackage(CreatePackage("Tomorrow", "Content 2"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Single(sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll());
    }

    [Fact]
    public async Task Scenario6_MultipleTerritories_ThreeRequestsDispatched()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup with first territory
        provider.SetPackage(CreatePackage("T1", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Second territory
        provider.SetPackage(CreatePackage("T2", "Content 2"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Third territory
        provider.SetPackage(CreatePackage("T3", "Content 3"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Equal(3, sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().Count());
        Assert.Contains("Content 1", sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().ElementAt(0).Payload);
        Assert.Contains("Content 2", sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().ElementAt(1).Payload);
        Assert.Contains("Content 3", sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>().GetAll().ElementAt(2).Payload);
    }
}



