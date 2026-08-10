using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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

public class Program
{
    public static int Main(string[] args)
    {
        var isMigrate = args.Contains("--migrate");

        var builder = WebApplication.CreateBuilder(args);

        // Logging
        builder.Services.AddLogging(configure => configure.AddConsole());

        // Database
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Database=svitlosk;Username=postgres;Password=postgres";
        
        builder.Services.AddDbContext<SvitloSkDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Domain
        builder.Services.AddSingleton<IEditionFactory, EditionFactory>();

        // Core
        builder.Services.AddSingleton<IEditorialDecisionEngine, EditorialDecisionEngine>();
        builder.Services.AddSingleton<ISituationModel, SituationModel>();
        builder.Services.AddSingleton<IReasoningModel, ReasoningModel>();

        // Execution
        builder.Services.AddScoped<IEditorialOrderingStrategy, CanonicalOrderingStrategy>();
        builder.Services.AddScoped<IEditionAssembly, EditionAssembly>();
        builder.Services.AddScoped<IGraphicPublisher, GraphicPublisher>();

        // Channels
        builder.Services.AddSingleton<IPublicationPipeline, PublicationPipeline>();

        // Runtime
        builder.Services.AddScoped<IExternalPublicationIdentityResolver, EfExternalPublicationIdentityResolver>();
        builder.Services.AddScoped<IOutboxRepository, EfOutboxRepository>();
        builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        if (!isMigrate)
        {
            // Workers (DO NOT start in --migrate mode)
            builder.Services.AddScoped<ISynchronizationEngine, SynchronizationEngine>();
            builder.Services.AddHostedService<SynchronizationWorker>();
            builder.Services.AddHostedService<OutboxDispatcherWorker>();
        }

        // Runtime Persistence
        builder.Services.AddScoped<IEditionRepository, EfEditionRepository>();
        builder.Services.Configure<InputSourcesOptions>(builder.Configuration.GetSection("InputSources"));
        builder.Services.AddHttpClient<IInputPackageProvider, RealOutagesSkInputPackageProvider>();

        // Adapters
        builder.Services.AddOptions<TelegramOptions>()
            .Bind(builder.Configuration.GetSection("Telegram"))
            .Validate(opts => !string.IsNullOrWhiteSpace(opts.BotToken) && opts.BotToken != "YOUR_BOT_TOKEN_HERE", "Invalid Telegram BotToken.")
            .Validate(opts => !string.IsNullOrWhiteSpace(opts.TargetChatId) && opts.TargetChatId != "YOUR_CHAT_ID_HERE", "Invalid Telegram TargetChatId.")
            .ValidateOnStart();

        builder.Services.AddHttpClient<IPublicationPort, TelegramAdapter>();

        // Keep InMemoryDispatcher for tests, but DO NOT map it to IPublicationPort in production
        builder.Services.AddSingleton<InMemoryDispatcher>();

        // Health Checks
        builder.Services.AddHealthChecks()
            .AddDbContextCheck<SvitloSkDbContext>(tags: new[] { "ready" });

        var app = builder.Build();

        if (isMigrate)
        {
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SvitloSkDbContext>();
                db.Database.Migrate();
                logger.LogInformation("Migration successful.");
                return 0; // exit success
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Migration failed.");
                return 1; // exit failure
            }
        }

        // Apply migrations automatically ONLY for development convenience (DO NOT apply automatically in Production)
        if (app.Environment.IsDevelopment())
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SvitloSkDbContext>();
            db.Database.Migrate();
        }

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false // Liveness just indicates HTTP pipeline is alive
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });

        Console.WriteLine("READY FOR BUSINESS IMPLEMENTATION");

        app.Run();
        return 0;
    }
}
