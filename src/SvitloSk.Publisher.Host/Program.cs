using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;

namespace SvitloSk.Publisher.Host;

class Program
{
    static void Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Logging
                services.AddLogging(configure => configure.AddConsole());

                // Domain
                services.AddSingleton<IEditionFactory, EditionFactory>();

                // Core
                services.AddSingleton<IEditorialDecisionEngine, EditorialDecisionEngine>();
                services.AddSingleton<ISituationModel, SituationModel>();
                services.AddSingleton<IReasoningModel, ReasoningModel>();

                // Execution
                services.AddScoped<IEditorialOrderingStrategy, CanonicalOrderingStrategy>();
                services.AddScoped<IEditionAssembly, EditionAssembly>();
                services.AddScoped<IGraphicPublisher, GraphicPublisher>();

                // Channels
                services.AddSingleton<IPublicationPipeline, PublicationPipeline>();

                // Runtime
                services.AddSingleton<IExternalPublicationIdentityResolver, InMemoryExternalPublicationIdentityResolver>();
                services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();
                services.AddHostedService<SynchronizationWorker>();

                // Runtime (in-memory defaults for production readiness where no external systems are defined)
                services.AddSingleton<IEditionRepository, InMemoryEditionRepository>();
                services.AddSingleton<IInputPackageProvider, InMemoryInputPackageProvider>();

                // Adapters
                services.AddSingleton<InMemoryDispatcher>();
                services.AddSingleton<IPublicationPort>(sp => sp.GetRequiredService<InMemoryDispatcher>());
            });

        var host = builder.Build();

        Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION");

        host.Run();
    }
}
