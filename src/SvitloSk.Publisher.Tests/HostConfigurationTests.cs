using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Channels;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class HostConfigurationTests
{
    private IHostBuilder CreateHostBuilder(Dictionary<string, string?> inMemorySettings)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(inMemorySettings);
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOptions<TelegramOptions>()
                    .Bind(context.Configuration.GetSection("Telegram"))
                    .Validate(opts => !string.IsNullOrWhiteSpace(opts.BotToken), "Telegram BotToken is required.")
                    .Validate(opts => !string.IsNullOrWhiteSpace(opts.TargetChatId), "Telegram TargetChatId is required.")
                    .ValidateOnStart();

                services.AddHttpClient<IPublicationPort, TelegramAdapter>();
                services.AddSingleton<InMemoryDispatcher>(); // Should not be bound to IPublicationPort
            });
    }

    [Fact]
    public void IPublicationPort_ResolvesToTelegramAdapter_InProductionHost()
    {
        var settings = new Dictionary<string, string?>
        {
            {"Telegram:BotToken", "test-token"},
            {"Telegram:TargetChatId", "test-chat"}
        };

        var host = CreateHostBuilder(settings).Build();

        using var scope = host.Services.CreateScope();
        var port = scope.ServiceProvider.GetRequiredService<IPublicationPort>();

        Assert.IsType<TelegramAdapter>(port);
    }

    [Fact]
    public void MissingTelegramConfiguration_ThrowsOptionsValidationException()
    {
        var settings = new Dictionary<string, string?>
        {
            // Missing BotToken
            {"Telegram:TargetChatId", "test-chat"}
        };

        var host = CreateHostBuilder(settings).Build();

        using var scope = host.Services.CreateScope();
        
        Assert.Throws<OptionsValidationException>(() => 
            scope.ServiceProvider.GetRequiredService<IPublicationPort>()
        );
    }
}
