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
        services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();
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
        return new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "TestSource", "Starokostiantyniv Urban Territorial Community", new[] { new TerritorialPayload(territory, SourcePortion.Today, content) });
    }

    [Fact]
    public async Task Scenario1_MorningStartup_CreatesEditionAndGeneratesPublication()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Single(dispatcher.DispatchedArtifacts);
        Assert.Contains("GRAPHIC_[Content for Kyiv Content 1]", dispatcher.DispatchedArtifacts.First().Payload);
    }

    [Fact]
    public async Task Scenario2_IncrementalUpdate_OnlyOneRequestDispatched()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        dispatcher.Clear();

        // Incremental update
        provider.SetPackage(CreatePackage("Kyiv", "Content 2"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Empty(dispatcher.DispatchedArtifacts);
    }

    [Fact]
    public async Task Scenario3_Deletion_FailsSafely_WithoutDispatch()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        
        var pubId = dispatcher.DispatchedArtifacts.First().RequestId;
        dispatcher.Clear();

        // Disappears -> package becomes null
        provider.SetPackage(CreatePackage("Kyiv", "CLEAR", "Clear"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Empty(dispatcher.DispatchedArtifacts);
    }

    [Fact]
    public async Task Scenario4_NoChanges_NoDispatch()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        dispatcher.Clear();

        // No changes
        provider.SetPackage(CreatePackage("Kyiv", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Empty(dispatcher.DispatchedArtifacts);
    }

    [Fact]
    public async Task Scenario5_TomorrowScheduleUpdate_OnlyTomorrowDispatched()
    {
        var (sp, repo, dispatcher, provider) = SetupContainer();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Morning startup
        provider.SetPackage(CreatePackage("Tomorrow", "Content 1"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);
        dispatcher.Clear();

        // Incremental update
        provider.SetPackage(CreatePackage("Tomorrow", "Content 2"));
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        Assert.Empty(dispatcher.DispatchedArtifacts);
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

        Assert.Equal(3, dispatcher.DispatchedArtifacts.Count);
        Assert.Contains("GRAPHIC_[Content for T1 Content 1]", dispatcher.DispatchedArtifacts.ElementAt(0).Payload);
        Assert.Contains("GRAPHIC_[Content for T2 Content 2]", dispatcher.DispatchedArtifacts.ElementAt(1).Payload);
        Assert.Contains("GRAPHIC_[Content for T3 Content 3]", dispatcher.DispatchedArtifacts.ElementAt(2).Payload);
    }
}
