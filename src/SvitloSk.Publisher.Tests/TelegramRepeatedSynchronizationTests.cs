using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Runtime.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class TelegramRepeatedSynchronizationTests
{
    private class CountingMockHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        private int _messageIdCounter = 1000;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            _messageIdCounter++;
            var json = $"{{\"ok\":true,\"result\":{{\"message_id\":{_messageIdCounter},\"chat\":{{\"id\":-100123456789}}}}}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }

        public void ResetCount()
        {
            RequestCount = 0;
        }
    }

    private ServiceProvider SetupServices(CountingMockHttpMessageHandler mockHandler)
    {
        var services = new ServiceCollection();
        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new TelegramOptions { BotToken = "test_token", TargetChatId = "-100123456789" });

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
        services.AddSingleton<IEditionRepository, InMemoryEditionRepository>();
        services.AddSingleton<IInputPackageProvider, TestInputPackageProvider>();

        var telegramAdapter = new TelegramAdapter(httpClient, options, NullLogger<TelegramAdapter>.Instance);
        services.AddSingleton<IPublicationPort>(telegramAdapter);

        return services.BuildServiceProvider();
    }

    private async Task DispatchOutbox(IServiceProvider sp)
    {
        var outbox = sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>();
        var pipeline = sp.GetRequiredService<IPublicationPipeline>();
        var resolver = sp.GetRequiredService<IExternalPublicationIdentityResolver>();

        var pending = outbox.GetPendingMessages(100);
        foreach (var msg in pending.ToList())
        {
            var req = new PublicationRequest(msg.PublicationId, msg.Payload, msg.OperationType, msg.ExternalIdentity, msg.ArtifactType);
            var result = await pipeline.DispatchAsync(req, CancellationToken.None);
            if (result != null && msg.OperationType == TransportOperation.CREATE)
            {
                resolver.RecordExternalIdentity(msg.PublicationId, result.MessageId);
            }
            else if (msg.OperationType == TransportOperation.DELETE)
            {
                resolver.RemoveExternalIdentity(msg.PublicationId);
            }
            msg.Status = OutboxOperationStatus.Completed;
        }
    }

    [Fact]
    public async Task RepeatedIdenticalInput_ProducesNoDuplicateTelegramRequests()
    {
        // Setup
        var mockHandler = new CountingMockHttpMessageHandler();
        var sp = SetupServices(mockHandler);
        var engine = sp.GetRequiredService<ISynchronizationEngine>();
        var packageProvider = (TestInputPackageProvider)sp.GetRequiredService<IInputPackageProvider>();
        var repository = sp.GetRequiredService<IEditionRepository>();

        var packageId = Guid.NewGuid();
        var payload = "Identical Payload";
        packageProvider.SetPackage(new InputPackage(packageId, DateTimeOffset.UtcNow, "src", "Starokostiantyniv Urban Territorial Community", new[] { new Event("Alpha", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));

        // Run 1 - Initial publish
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox(sp);
        
        var initialRequestCount = mockHandler.RequestCount;
        Assert.Equal(1, initialRequestCount); // Tomorrow not processed, only "Alpha"
        
        var edition1 = repository.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var initialPubCount = edition1!.Packages.SelectMany(p => p.Publications).Count();

        // Reset
        mockHandler.ResetCount();

        // Run 2 - Exact same input
        // Using the same provider state, simulating the same package arriving or polling yielding same result
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox(sp);

        // Verify No duplicate publication
        Assert.Equal(0, mockHandler.RequestCount);

        var edition2 = repository.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var newPubCount = edition2!.Packages.SelectMany(p => p.Publications).Count();
        Assert.Equal(initialPubCount, newPubCount); // Edition state remains correct, no duplicate publication added
    }

    [Fact]
    public async Task ChangedInput_FailsExplicitlyOnUpdate_PreservesIdentity()
    {
        // Setup
        var mockHandler = new CountingMockHttpMessageHandler();
        var sp = SetupServices(mockHandler);
        var engine = sp.GetRequiredService<ISynchronizationEngine>();
        var packageProvider = (TestInputPackageProvider)sp.GetRequiredService<IInputPackageProvider>();
        var identityResolver = sp.GetRequiredService<IExternalPublicationIdentityResolver>();

        var packageId = Guid.NewGuid();
        packageProvider.SetPackage(new InputPackage(packageId, DateTimeOffset.UtcNow, "src", "Starokostiantyniv Urban Territorial Community", new[] { new Event("Beta", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));

        // Run 1 - Publish
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox(sp);
        
        // Find the publication and get its identity
        var repository = sp.GetRequiredService<IEditionRepository>();
        var pub = repository.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow))!.Packages.SelectMany(p => p.Publications).First(p => p.TerritoryId == "Beta");
        var initialExtId = identityResolver.ResolveExternalIdentity(pub.Id.ToString());
        Assert.NotNull(initialExtId);
        
        // Reset counter
        mockHandler.ResetCount();

        // Run 2 - Changed input
        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Beta", new[] { new Event("Beta", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        
        // This should run the UPDATE logic in SynchronizationEngine, which detects existing identity and catches NotSupportedException.
        // It shouldn't crash, but it shouldn't send anything to Telegram.
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox(sp);

        Assert.Equal(0, mockHandler.RequestCount);

        // Verify the original identity is preserved
        var finalExtId = identityResolver.ResolveExternalIdentity(pub.Id.ToString());
        Assert.Equal(initialExtId, finalExtId);
    }
}

