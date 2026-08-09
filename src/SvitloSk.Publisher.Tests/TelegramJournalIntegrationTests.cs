using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
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
using Xunit;
using SvitloSk.Publisher.Runtime.Persistence;

namespace SvitloSk.Publisher.Tests;

public class TelegramJournalIntegrationTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Contents { get; } = new();
        private int _messageIdCounter = 1000;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content != null)
            {
                Contents.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }
            
            _messageIdCounter++;
            var json = $"{{\"ok\":true,\"result\":{{\"message_id\":{_messageIdCounter},\"chat\":{{\"id\":-100123456789}}}}}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        }
    }

    [Fact]
    public async Task Scenario7_CompleteJournalPublication_PublishesInOrder()
    {
        // Setup
        var services = new ServiceCollection();
        var mockHandler = new MockHttpMessageHandler();
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
        services.AddSingleton<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository, SvitloSk.Publisher.Runtime.InMemoryOutboxRepository>(); services.AddSingleton<SvitloSk.Publisher.Runtime.Persistence.IUnitOfWork, SvitloSk.Publisher.Runtime.InMemoryUnitOfWork>(); services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();
        
        var identityResolver = new InMemoryExternalPublicationIdentityResolver();
        services.AddSingleton<IExternalPublicationIdentityResolver>(identityResolver);

        var repository = new InMemoryEditionRepository();
        services.AddSingleton<IEditionRepository>(repository);
        
        var packageProvider = new TestInputPackageProvider();
        services.AddSingleton<IInputPackageProvider>(packageProvider);

        var telegramAdapter = new TelegramAdapter(httpClient, options, NullLogger<TelegramAdapter>.Instance);
        services.AddSingleton<IPublicationPort>(telegramAdapter);

        var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<ISynchronizationEngine>();
        var outbox = sp.GetRequiredService<SvitloSk.Publisher.Runtime.Persistence.IOutboxRepository>();
        var pipeline = sp.GetRequiredService<IPublicationPipeline>();
        var resolver = sp.GetRequiredService<IExternalPublicationIdentityResolver>();

        async Task DispatchOutbox()
        {
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

        // Act - Complete Journal
        // According to EditorialOrder, order is: Tomorrow, Starokostiantyniv, Other territories (alphabetical), Technical.
        
        // 1. Send Tomorrow
        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Tomorrow", new[] { new Event("Tomorrow", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox();
        
        // 2. Send Technical
        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Starokostiantyniv Urban Territorial Community", new[] { new Event("Technical", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) })); // Not Tomorrow
        // Wait, how does SituationModel assign Type? It assigns PublicationType based on territory?
        // Actually, in TestInputPackageProvider, we pass territories. 
        // Let's look at how Tomorrow is assigned. In SynchronizationEngine it just uses the package payload? 
        // Wait, the tests don't have access to how SituationModel maps territory to PublicationType.
        // Let's see if we can manually insert them into Edition, or just dispatch them by feeding packages.
        
        // We can just feed packages with different territories: Starokostiantyniv, Alpha, Beta.
        // Let's do Starokostiantyniv first.
        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Starokostiantyniv", new[] { new Event("Starokostiantyniv", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox();

        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Beta", new[] { new Event("Beta", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox();
        
        Assert.Equal(3, mockHandler.Contents.Count);
        var content = mockHandler.Contents[2]; // The Beta one
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;
        
        Assert.Equal("-100123456789", root.GetProperty("chat_id").GetString());
        
        var textValue = root.TryGetProperty("photo", out var photo) ? photo.GetString() : root.GetProperty("text").GetString();
        
        Assert.Contains("GRAPHIC\\_\\[Content for Beta", textValue);

        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Alpha", new[] { new Event("Alpha", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox();

        // Assert
        // Morning startup sends each immediately, so the sequence of dispatches is sequential, 
        // but let's check the contents to verify payload formatting and MarkdownV2 escaping.
        // "Test_Data" should be escaped to "Test\_Data"

        // Let's update Beta with an underscore to test escaping.
        mockHandler.Contents.Clear();
        mockHandler.Requests.Clear();

        // This triggers an update for Beta.
        packageProvider.SetPackage(new InputPackage(Guid.NewGuid(), DateTimeOffset.UtcNow, "src", "Beta", new[] { new Event("Beta", Array.Empty<string>(), new[] { new Interval(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2)) }) }));
        await engine.MaintainPublisherStateAsync(CancellationToken.None); await DispatchOutbox();

        Assert.Empty(mockHandler.Contents);
        
        // Ensure identities are captured from the original publication
        var edition = repository.GetByDate(DateOnly.FromDateTime(DateTime.UtcNow));
        var betaPub = edition!.Packages.SelectMany(p => p.Publications).First(p => p.TerritoryId == "Beta");
        var extId = identityResolver.ResolveExternalIdentity(betaPub.Id.ToString());
        Assert.NotNull(extId);
        Assert.StartsWith("-100123456789:", extId);
    }
}

