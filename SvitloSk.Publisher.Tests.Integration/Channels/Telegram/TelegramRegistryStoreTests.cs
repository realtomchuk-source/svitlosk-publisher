using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Infrastructure.Channels.Telegram;
using SvitloSk.Publisher.Infrastructure.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests.Unit.Channels.Telegram;

public class TelegramRegistryStoreTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _legacyPath;
    private readonly string _telegramPath;
    private readonly IRegistryStore _innerStore;

    public TelegramRegistryStoreTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "SvitloSk_TelegramRegistryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _legacyPath = Path.Combine(_testDir, "production_registry.json");
        _telegramPath = Path.Combine(_testDir, "telegram_registry.json");
        _innerStore = new JsonRegistryStore(new FileSystemAtomicWriter());
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    [Fact]
    public async Task LoadAsync_WhenLegacyExistsAndTelegramDoesNotExist_MigratesSeamlessly()
    {
        // Arrange: Legacy file exists with 2 records
        var legacyRecords = new List<RegistryPublicationRecord>
        {
            new(Guid.NewGuid(), "journal_header", "12345", "hash1", "SENT", "Text"),
            new(Guid.NewGuid(), "system_status", "12346", "hash2", "SENT", "Text")
        };
        var legacyModel = new RegistryModel(1, "2026-09-13", "ACTIVE", legacyRecords);
        await _innerStore.SaveAsync(_legacyPath, legacyModel);

        var channelStore = new TelegramRegistryStore(_innerStore, _telegramPath, _legacyPath);

        // Act
        var loaded = await channelStore.LoadAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("2026-09-13", loaded.EditionDate);
        Assert.Equal(2, loaded.Publications.Count);
        Assert.True(File.Exists(_telegramPath), "telegram_registry.json should have been created via auto-migration");

        // Reload directly to verify it was saved
        var directlyLoaded = await _innerStore.LoadAsync(_telegramPath);
        Assert.NotNull(directlyLoaded);
        Assert.Equal(2, directlyLoaded.Publications.Count);
    }

    [Fact]
    public async Task SaveAsync_SavesToTelegramPathAndKeepsLegacySynchronized()
    {
        var records = new List<RegistryPublicationRecord>
        {
            new(Guid.NewGuid(), "territory_1", "9999", "hashX", "SENT", "Text")
        };
        var model = new RegistryModel(1, "2026-09-13", "ACTIVE", records);

        // Create legacy file first
        await _innerStore.SaveAsync(_legacyPath, model);

        var channelStore = new TelegramRegistryStore(_innerStore, _telegramPath, _legacyPath);

        // Update record
        var updatedRecords = new List<RegistryPublicationRecord>
        {
            new(records[0].PublisherArtifactId, "territory_1", "9999", "hashX2", "UPDATED", "Text")
        };
        var updatedModel = new RegistryModel(1, "2026-09-13", "ACTIVE", updatedRecords);

        // Act
        await channelStore.SaveAsync(updatedModel);

        // Assert
        var fromTelegram = await _innerStore.LoadAsync(_telegramPath);
        var fromLegacy = await _innerStore.LoadAsync(_legacyPath);

        Assert.NotNull(fromTelegram);
        Assert.NotNull(fromLegacy);
        Assert.Equal("UPDATED", fromTelegram.Publications[0].TransmissionState);
        Assert.Equal("UPDATED", fromLegacy.Publications[0].TransmissionState);
    }
}
