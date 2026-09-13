using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;

namespace SvitloSk.Publisher.Infrastructure.Channels.Telegram;

/// <summary>
/// Dedicated channel-specific registry store for Telegram channel.
/// Manages telegram_registry.json with seamless auto-migration from legacy production_registry.json.
/// </summary>
public class TelegramRegistryStore : IChannelRegistryStore
{
    private readonly IRegistryStore _innerStore;
    private readonly string _registryPath;
    private readonly string _legacyProductionPath;

    public string ChannelName => "Telegram";
    public string RegistryPath => _registryPath;

    public TelegramRegistryStore(
        IRegistryStore innerStore,
        string? registryPath = null,
        string? legacyProductionPath = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));

        string baseDir = "local/registry";
        _registryPath = !string.IsNullOrWhiteSpace(registryPath) 
            ? registryPath 
            : Path.Combine(baseDir, "telegram_registry.json");

        _legacyProductionPath = !string.IsNullOrWhiteSpace(legacyProductionPath)
            ? legacyProductionPath
            : Path.Combine(baseDir, "production_registry.json");
    }

    public async Task<RegistryModel?> LoadAsync(CancellationToken cancellationToken = default)
    {
        // 1. If telegram_registry.json exists, load it directly
        if (File.Exists(_registryPath))
        {
            return await _innerStore.LoadAsync(_registryPath, cancellationToken).ConfigureAwait(false);
        }

        // 2. Seamless Auto-Migration:
        // If telegram_registry.json does not exist, but legacy production_registry.json exists,
        // we copy it over to preserve historical message IDs and avoid channel re-publishing.
        if (File.Exists(_legacyProductionPath))
        {
            Console.WriteLine($"[INFO] Migrating legacy registry '{_legacyProductionPath}' -> '{_registryPath}'");
            var legacyModel = await _innerStore.LoadAsync(_legacyProductionPath, cancellationToken).ConfigureAwait(false);
            if (legacyModel != null)
            {
                await _innerStore.SaveAsync(_registryPath, legacyModel, cancellationToken).ConfigureAwait(false);
                return legacyModel;
            }
        }

        return null;
    }

    public async Task SaveAsync(RegistryModel model, CancellationToken cancellationToken = default)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        // Save to dedicated telegram_registry.json
        await _innerStore.SaveAsync(_registryPath, model, cancellationToken).ConfigureAwait(false);

        // Also keep legacy file in sync if it exists or until full migration is complete
        try
        {
            if (File.Exists(_legacyProductionPath) || _registryPath.Equals(_legacyProductionPath, StringComparison.OrdinalIgnoreCase))
            {
                await _innerStore.SaveAsync(_legacyProductionPath, model, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[WARN] Could not sync legacy production registry file: {ex.Message}");
        }
    }

    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(_registryPath) || File.Exists(_legacyProductionPath));
    }
}
