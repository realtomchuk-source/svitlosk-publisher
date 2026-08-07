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

namespace SvitloSk.Publisher.Tests;

public class EndToEndScenarioTests
{
    [Fact]
    public async Task CompleteBusinessScenario_ExecutesSuccessfully()
    {
        // Arrange
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

        var repository = new InMemoryEditionRepository();
        services.AddSingleton<IEditionRepository>(repository);
        
        var packageProvider = new InMemoryInputPackageProvider();
        services.AddSingleton<IInputPackageProvider>(packageProvider);

        var dispatcher = new InMemoryDispatcher();
        services.AddSingleton<InMemoryDispatcher>(dispatcher);
        services.AddSingleton<IPublicationPort>(dispatcher);

        var serviceProvider = services.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<ISynchronizationEngine>();

        // Act
        await engine.MaintainPublisherStateAsync(CancellationToken.None);

        // Assert
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var edition = repository.GetByDate(today);
        
        Assert.NotNull(edition);
        Assert.Equal(SvitloSk.Publisher.Domain.EditionState.Active, edition.State);
        Assert.NotEmpty(edition.Packages.SelectMany(p => p.Publications));

        var dispatchedRequests = dispatcher.DispatchedRequests;
        Assert.NotEmpty(dispatchedRequests);
        Assert.Contains("Content for inmemory_territory", dispatchedRequests.First().Edition);
    }
}
