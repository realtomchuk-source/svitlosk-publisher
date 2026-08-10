using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SvitloSk.Publisher.Adapters.Telegram;
using SvitloSk.Publisher.Host;
using Xunit;

namespace SvitloSk.Publisher.Tests;

public class ConfigurationValidationTests
{
    private IServiceProvider BuildServiceProviderWithTelegramConfig(Dictionary<string, string> config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config!)
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection("Telegram"))
            .Validate(opts => !string.IsNullOrWhiteSpace(opts.BotToken) && opts.BotToken != "YOUR_BOT_TOKEN_HERE", "Invalid Telegram BotToken.")
            .Validate(opts => !string.IsNullOrWhiteSpace(opts.TargetChatId) && opts.TargetChatId != "YOUR_CHAT_ID_HERE", "Invalid Telegram TargetChatId.")
            .ValidateOnStart();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void EmptyBotToken_ThrowsOptionsValidationException()
    {
        var sp = BuildServiceProviderWithTelegramConfig(new Dictionary<string, string>
        {
            { "Telegram:BotToken", "" },
            { "Telegram:TargetChatId", "valid-chat-id" }
        });

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TelegramOptions>>().Value);
        Assert.Contains("Invalid Telegram BotToken.", ex.Message);
    }

    [Fact]
    public void PlaceholderBotToken_ThrowsOptionsValidationException()
    {
        var sp = BuildServiceProviderWithTelegramConfig(new Dictionary<string, string>
        {
            { "Telegram:BotToken", "YOUR_BOT_TOKEN_HERE" },
            { "Telegram:TargetChatId", "valid-chat-id" }
        });

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TelegramOptions>>().Value);
        Assert.Contains("Invalid Telegram BotToken.", ex.Message);
        Assert.DoesNotContain("YOUR_BOT_TOKEN_HERE", ex.Message);
    }

    [Fact]
    public void EmptyTargetChatId_ThrowsOptionsValidationException()
    {
        var sp = BuildServiceProviderWithTelegramConfig(new Dictionary<string, string>
        {
            { "Telegram:BotToken", "valid-token" },
            { "Telegram:TargetChatId", "" }
        });

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TelegramOptions>>().Value);
        Assert.Contains("Invalid Telegram TargetChatId.", ex.Message);
    }

    [Fact]
    public void PlaceholderTargetChatId_ThrowsOptionsValidationException()
    {
        var sp = BuildServiceProviderWithTelegramConfig(new Dictionary<string, string>
        {
            { "Telegram:BotToken", "valid-token" },
            { "Telegram:TargetChatId", "YOUR_CHAT_ID_HERE" }
        });

        var ex = Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<TelegramOptions>>().Value);
        Assert.Contains("Invalid Telegram TargetChatId.", ex.Message);
        Assert.DoesNotContain("YOUR_CHAT_ID_HERE", ex.Message);
    }

    [Fact]
    public void ValidConfig_Succeeds()
    {
        var sp = BuildServiceProviderWithTelegramConfig(new Dictionary<string, string>
        {
            { "Telegram:BotToken", "valid-token" },
            { "Telegram:TargetChatId", "valid-chat-id" }
        });

        var options = sp.GetRequiredService<IOptions<TelegramOptions>>().Value;
        Assert.Equal("valid-token", options.BotToken);
        Assert.Equal("valid-chat-id", options.TargetChatId);
    }
}
