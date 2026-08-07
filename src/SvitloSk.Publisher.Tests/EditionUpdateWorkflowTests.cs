using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class EditionUpdateWorkflowTests
{
    private ServiceProvider BuildServiceProvider(IEditionRepository repository, IInputPackageProvider packageProvider, InMemoryDispatcher dispatcher)
    {
        var services = new ServiceCollection();

        services.AddSingleton<ILogger<SynchronizationEngine>>(NullLogger<SynchronizationEngine>.Instance);
        
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

        services.AddSingleton(repository);
        services.AddSingleton(packageProvider);
        services.AddSingleton(dispatcher);
        services.AddSingleton<IPublicationPort>(dispatcher);

        return services.BuildServiceProvider();
    }

    private class TestInputPackageProvider : IInputPackageProvider
    {
        private readonly InputPackage _package;
        public TestInputPackageProvider(InputPackage package) => _package = package;
        public Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken) => Task.FromResult(_package);
    }

    [Fact]
    public async Task UpdateWorkflow_IdenticalPackage_NoChanges()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = new InMemoryEditionRepository();
        
        var edition = new EditionFactory().Create(today);
        edition.Activate();
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        var pub = new Publication(Guid.NewGuid(), "Staro", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash_1");
        pkg.AddPublication(pub);
        repository.Save(edition);

        var package = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Staro", "hash_1"); // same hash
        var provider = new TestInputPackageProvider(package);

        var dispatcher = new InMemoryDispatcher();
        var sp = BuildServiceProvider(repository, provider, dispatcher);
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        Assert.Empty(dispatcher.DispatchedRequests);
        Assert.Single(repository.GetByDate(today)!.Packages.SelectMany(p => p.Publications));
    }

    [Fact]
    public async Task UpdateWorkflow_OneChangedPublication_UpdatesAndDispatches()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = new InMemoryEditionRepository();
        
        var edition = new EditionFactory().Create(today);
        edition.Activate();
        var pkg = new PublicationPackage(Guid.NewGuid(), "Default Package");
        edition.AddPackage(pkg);
        var pub = new Publication(Guid.NewGuid(), "Staro", PublicationClassification.Persistent, PublicationType.Text, DateTimeOffset.UtcNow, "hash_1");
        pkg.AddPublication(pub);
        repository.Save(edition);

        var package = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Staro", "hash_2"); // changed hash
        var provider = new TestInputPackageProvider(package);

        var dispatcher = new InMemoryDispatcher();
        var sp = BuildServiceProvider(repository, provider, dispatcher);
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        Assert.Single(dispatcher.DispatchedRequests);
        var savedEdition = repository.GetByDate(today);
        Assert.Single(savedEdition!.Packages);
        Assert.Single(savedEdition.Packages.First().Publications);
        Assert.Equal("hash_2", savedEdition.Packages.First().Publications.First().ContentHash);
    }

    [Fact]
    public async Task UpdateWorkflow_OneNewPublication_CreatesAndDispatches()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var repository = new InMemoryEditionRepository();
        
        var edition = new EditionFactory().Create(today);
        edition.Activate();
        repository.Save(edition);

        var package = new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Staro", "hash_3"); // new pub
        var provider = new TestInputPackageProvider(package);

        var dispatcher = new InMemoryDispatcher();
        var sp = BuildServiceProvider(repository, provider, dispatcher);
        var engine = sp.GetRequiredService<ISynchronizationEngine>();

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        Assert.Single(dispatcher.DispatchedRequests);
        var savedEdition = repository.GetByDate(today);
        Assert.Single(savedEdition!.Packages);
        Assert.Single(savedEdition.Packages.First().Publications);
        Assert.Equal("Staro", savedEdition.Packages.First().Publications.First().TerritoryId);
        Assert.Equal("hash_3", savedEdition.Packages.First().Publications.First().ContentHash);
    }
}
