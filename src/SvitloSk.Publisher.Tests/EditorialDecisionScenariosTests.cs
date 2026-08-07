using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class EditorialDecisionScenariosTests
{
    private (IServiceProvider, DummyEditionRepository, InMemoryDispatcher, DummyInputPackageProvider) SetupContainer(InputPackage initialPackage)
    {
        var services = new ServiceCollection();

        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<SynchronizationEngine>>(NullLogger<SynchronizationEngine>.Instance);
        services.AddSingleton<IEditionFactory, EditionFactory>();
        services.AddSingleton<IEditorialDecisionEngine, EditorialDecisionEngine>();
        services.AddSingleton<ISituationModel, SituationModel>();
        services.AddSingleton<IReasoningModel, ReasoningModel>();
        services.AddScoped<IEditorialOrderingStrategy, CanonicalOrderingStrategy>();
        services.AddScoped<IEditionAssembly, EditionAssembly>();
        services.AddScoped<IGraphicPublisher, GraphicPublisher>();
        services.AddScoped<IGraphicAssembly, GraphicAssembly>();
        services.AddSingleton<IPublicationPipeline, PublicationPipeline>();
        services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();

        var repository = new DummyEditionRepository();
        services.AddSingleton<IEditionRepository>(repository);

        var packageProvider = new DummyInputPackageProvider(initialPackage);
        services.AddSingleton<IInputPackageProvider>(packageProvider);

        var dispatcher = new InMemoryDispatcher();
        services.AddSingleton<InMemoryDispatcher>(dispatcher);
        services.AddSingleton<IPublicationPort>(dispatcher);

        return (services.BuildServiceProvider(), repository, dispatcher, packageProvider);
    }

    private class DummyInputPackageProvider : IInputPackageProvider
    {
        public InputPackage CurrentPackage { get; set; }

        public DummyInputPackageProvider(InputPackage initialPackage)
        {
            CurrentPackage = initialPackage;
        }

        public Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentPackage);
        }
    }

    [Fact]
    public async Task Scenario1_NewOutageAppears_CreatesPublication()
    {
        var package = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "Payload1");
        var (provider, repo, dispatcher, pkgProvider) = SetupContainer(package);
        var engine = provider.GetRequiredService<ISynchronizationEngine>();

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var edition = repo.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var pubs = edition!.Packages.SelectMany(p => p.Publications).ToList();
        
        Assert.Single(pubs);
        Assert.Equal("T1", pubs[0].TerritoryId);
        Assert.NotEmpty(dispatcher.DispatchedRequests);
    }

    [Fact]
    public async Task Scenario2_OutageDurationChanges_UpdatesPublication()
    {
        // 1. Initial State
        var package1 = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "Payload1");
        var (provider, repo, dispatcher, pkgProvider) = SetupContainer(package1);
        var engine = provider.GetRequiredService<ISynchronizationEngine>();
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // 2. Change Payload
        pkgProvider.CurrentPackage = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "Payload2");

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var edition = repo.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var pubs = edition!.Packages.SelectMany(p => p.Publications).ToList();
        
        Assert.Single(pubs);
        Assert.Equal("Payload2", pubs[0].ContentHash);
        Assert.True(dispatcher.DispatchedRequests.Count >= 2);
    }

    [Fact]
    public async Task Scenario3_OutageDisappears_RemovesPublication()
    {
        // 1. Initial State
        var package1 = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "Payload1");
        var (provider, repo, dispatcher, pkgProvider) = SetupContainer(package1);
        var engine = provider.GetRequiredService<ISynchronizationEngine>();
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // 2. Disappear (Clear State)
        pkgProvider.CurrentPackage = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "", PackageState: "Clear");

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var edition = repo.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var pubs = edition!.Packages.SelectMany(p => p.Publications).ToList();
        
        Assert.Empty(pubs);
        // Dispatcher will not have a second dispatch because the edition is now empty (pkg.Publications.Any() is false)
        Assert.Equal(1, dispatcher.DispatchedRequests.Count); 
    }

    [Fact]
    public async Task Scenario4_OnlyMetadataChanges_NoAction()
    {
        // 1. Initial State
        var package1 = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "T1", "Payload1");
        var (provider, repo, dispatcher, pkgProvider) = SetupContainer(package1);
        var engine = provider.GetRequiredService<ISynchronizationEngine>();
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        var dispatchCountBefore = dispatcher.DispatchedRequests.Count;

        // 2. Only Metadata Changes (Timestamp changed, but Payload same)
        pkgProvider.CurrentPackage = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(5), "src", "T1", "Payload1");

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var edition = repo.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var pubs = edition!.Packages.SelectMany(p => p.Publications).ToList();
        
        Assert.Single(pubs);
        Assert.Equal(dispatchCountBefore, dispatcher.DispatchedRequests.Count); // No dispatch because NO_ACTION
    }

    [Fact]
    public async Task Scenario5_TomorrowScheduleChanges_UpdatesTomorrowOnly()
    {
        // 1. Initial State
        var package1 = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Tomorrow", "TomorrowPayload1");
        var (provider, repo, dispatcher, pkgProvider) = SetupContainer(package1);
        var engine = provider.GetRequiredService<ISynchronizationEngine>();
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // 2. Change Tomorrow
        pkgProvider.CurrentPackage = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Tomorrow", "TomorrowPayload2");

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var edition = repo.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var pubs = edition!.Packages.SelectMany(p => p.Publications).ToList();
        
        Assert.Single(pubs);
        Assert.Equal("TomorrowPayload2", pubs[0].ContentHash);
        Assert.True(dispatcher.DispatchedRequests.Count >= 2);
    }
}
