// Source: Host Infrastructure
// Section: Startup

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

                // Core
                services.AddSingleton<IEditorialDecisionEngine, EditorialDecisionEngine>();
                services.AddSingleton<ISituationModel, SituationModel>();
                services.AddSingleton<IReasoningModel, ReasoningModel>();

                // Execution
                services.AddScoped<IEditionAssembly, EditionAssembly>();
                services.AddScoped<IGraphicPublisher, GraphicPublisher>();
                services.AddScoped<IGraphicAssembly, GraphicAssembly>();

                // Channels
                services.AddSingleton<IPublicationPipeline, PublicationPipeline>();

                // Runtime
                services.AddSingleton<ISynchronizationEngine, SynchronizationEngine>();
                services.AddHostedService<SynchronizationWorker>();

                // Stubs for runtime activation
                services.AddSingleton<IEditionRepository, DummyEditionRepository>();
                services.AddSingleton<IInputPackageProvider, DummyInputPackageProvider>();

                // Adapters
                services.AddTransient<IPublicationPort, TelegramAdapter>();
            });

        var host = builder.Build();

        Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION");

        host.Run();
    }
}
