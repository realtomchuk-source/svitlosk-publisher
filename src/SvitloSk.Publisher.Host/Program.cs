using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SvitloSk.Publisher.Core;
using SvitloSk.Publisher.Core.Reasoning;
using SvitloSk.Publisher.Execution;
using SvitloSk.Publisher.Channels;
using SvitloSk.Publisher.Runtime;
using SvitloSk.Publisher.Runtime.Persistence;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Factories;

namespace SvitloSk.Publisher.Host;

class Program
{
    static void Main(string[] args)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                // Logging
                services.AddLogging(configure => configure.AddConsole());

                // Database
                var connectionString = context.Configuration.GetConnectionString("DefaultConnection") 
                    ?? "Host=localhost;Database=svitlosk;Username=postgres;Password=postgres";
                
                services.AddDbContext<SvitloSkDbContext>(options =>
                    options.UseNpgsql(connectionString));

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
                services.AddScoped<IExternalPublicationIdentityResolver, EfExternalPublicationIdentityResolver>();
                services.AddScoped<IOutboxRepository, EfOutboxRepository>();
                services.AddScoped<IUnitOfWork, EfUnitOfWork>();

                // Workers
                services.AddScoped<ISynchronizationEngine, SynchronizationEngine>();
                services.AddHostedService<SynchronizationWorker>();
                services.AddHostedService<OutboxDispatcherWorker>();

                // Runtime Persistence
                services.AddScoped<IEditionRepository, EfEditionRepository>();
                services.Configure<OutagesSkOptions>(context.Configuration.GetSection("OutagesSk"));
                services.AddHttpClient<IInputPackageProvider, RealOutagesSkInputPackageProvider>();

                // Adapters
                services.AddSingleton<InMemoryDispatcher>();
                services.AddSingleton<IPublicationPort>(sp => sp.GetRequiredService<InMemoryDispatcher>());
            });

        var host = builder.Build();

        // Apply migrations automatically for development convenience (as specified in Phase 5)
        var env = host.Services.GetRequiredService<IHostEnvironment>();
        if (env.IsDevelopment())
        {
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SvitloSkDbContext>();
            db.Database.Migrate();
        }

        Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION");

        host.Run();
    }
}
