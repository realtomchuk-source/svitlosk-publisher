using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Infrastructure.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration.Persistence;

public class JsonRegistryStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly FileSystemAtomicWriter _atomicWriter;
    private readonly JsonRegistryStore _store;

    public JsonRegistryStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_StoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);

        _atomicWriter = new FileSystemAtomicWriter();
        _store = new JsonRegistryStore(_atomicWriter);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task D02_T01_LoadExistingValidJsonRegistry_ShouldSucceed()
    {
        string targetPath = Path.Combine(_testDirectory, "valid.json");
        string json = @"
{
  ""schema_version"": 1,
  ""edition_date"": ""2026-08-12"",
  ""status"": ""ACTIVE"",
  ""publications"": [
    {
      ""publisher_artifact_id"": ""9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"",
      ""territory_id"": ""starokostiantyniv"",
      ""telegram_message_id"": 12345,
      ""content_hash"": ""hash1"",
      ""transmission_state"": ""SENT""
    }
  ]
}";
        await File.WriteAllTextAsync(targetPath, json);

        var model = await _store.LoadAsync(targetPath);

        Assert.NotNull(model);
        Assert.Equal(1, model.SchemaVersion);
        Assert.Equal("2026-08-12", model.EditionDate);
        Assert.Equal("ACTIVE", model.Status);
        Assert.Single(model.Publications);
        Assert.Equal(Guid.Parse("9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"), model.Publications[0].PublisherArtifactId);
        Assert.Equal(12345, model.Publications[0].TelegramMessageId);
        Assert.Equal("SENT", model.Publications[0].TransmissionState);
    }

    [Fact]
    public async Task D02_T02_LoadMissingRegistry_ShouldReturnNull()
    {
        string path = Path.Combine(_testDirectory, "missing.json");
        var model = await _store.LoadAsync(path);

        Assert.Null(model);
    }

    [Fact]
    public async Task D02_T03_SaveRegistryToNewPath_ShouldPersistFile()
    {
        string path = Path.Combine(_testDirectory, "new_save.json");
        var pub = new RegistryPublicationRecord(Guid.NewGuid(), "staro", 555, "hash", "SENT");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });

        await _store.SaveAsync(path, model);

        Assert.True(File.Exists(path));
        var loaded = await _store.LoadAsync(path);
        Assert.NotNull(loaded);
        Assert.Equal("staro", loaded.Publications[0].TerritoryId);
    }

    [Fact]
    public async Task D02_T04_SaveExistingRegistry_ShouldAtomicallyReplaceIt()
    {
        string path = Path.Combine(_testDirectory, "replace.json");
        var pubOld = new RegistryPublicationRecord(Guid.NewGuid(), "staro", 111, "hash1", "SENT");
        var modelOld = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pubOld });
        await _store.SaveAsync(path, modelOld);

        var pubNew = new RegistryPublicationRecord(Guid.NewGuid(), "staro", 222, "hash2", "SENT");
        var modelNew = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pubNew });

        await _store.SaveAsync(path, modelNew);

        var loaded = await _store.LoadAsync(path);
        Assert.NotNull(loaded);
        Assert.Equal(222, loaded.Publications[0].TelegramMessageId);
    }

    [Fact]
    public async Task D02_T05_SaveAndLoadUkrainianContent_ShouldPreserveEncoding()
    {
        string path = Path.Combine(_testDirectory, "ukr.json");
        var pub = new RegistryPublicationRecord(Guid.NewGuid(), "Старокостянтинів", 333, "hash", "SENT");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });

        await _store.SaveAsync(path, model);

        var loaded = await _store.LoadAsync(path);
        Assert.NotNull(loaded);
        Assert.Equal("Старокостянтинів", loaded.Publications[0].TerritoryId);
    }

    [Fact]
    public async Task D02_T06_MalformedJson_ShouldThrowAndFailClosed()
    {
        string path = Path.Combine(_testDirectory, "corrupt.json");
        await File.WriteAllTextAsync(path, "{ corrupt json");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.LoadAsync(path));
    }

    [Fact]
    public async Task D02_T07_DeletedTombstone_ShouldSurviveRoundTrip()
    {
        string path = Path.Combine(_testDirectory, "deleted.json");
        var pub = new RegistryPublicationRecord(Guid.NewGuid(), "staro", null, "hash", "DELETED");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });

        await _store.SaveAsync(path, model);

        var loaded = await _store.LoadAsync(path);
        Assert.NotNull(loaded);
        Assert.Null(loaded.Publications[0].TelegramMessageId);
        Assert.Equal("DELETED", loaded.Publications[0].TransmissionState);
    }

    [Fact]
    public async Task D02_T10_SerializationStability_ShouldProduceIdenticalJsonStrings()
    {
        string path1 = Path.Combine(_testDirectory, "stable1.json");
        string path2 = Path.Combine(_testDirectory, "stable2.json");

        var pub = new RegistryPublicationRecord(Guid.Parse("9b1deb4d-3b7d-4bad-9bdd-2b0d7b3dcb6d"), "staro", 999, "hash", "SENT");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });

        await _store.SaveAsync(path1, model);
        await _store.SaveAsync(path2, model);

        string json1 = await File.ReadAllTextAsync(path1);
        string json2 = await File.ReadAllTextAsync(path2);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public async Task D02_T11_SaveFailure_ShouldLeaveExistingTargetUntouched()
    {
        string path = Path.Combine(_testDirectory, "target.json");
        var pub = new RegistryPublicationRecord(Guid.NewGuid(), "staro", 777, "hash", "SENT");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });
        await _store.SaveAsync(path, model);

        // Try to save to invalid nested path to trigger directory/file write failure
        string invalidPath = Path.Combine(path, "sub-dir", "invalid.json");
        await Assert.ThrowsAnyAsync<Exception>(() => _store.SaveAsync(invalidPath, model));

        // Original remains correct
        var loaded = await _store.LoadAsync(path);
        Assert.NotNull(loaded);
        Assert.Equal(777, loaded.Publications[0].TelegramMessageId);
    }

    [Fact]
    public async Task D02_T12_CanceledToken_ShouldCancelSaveAndThrow()
    {
        string path = Path.Combine(_testDirectory, "cancel.json");
        var pub = new RegistryPublicationRecord(Guid.NewGuid(), "staro", 888, "hash", "SENT");
        var model = new RegistryModel(1, "2026-08-12", "ACTIVE", new List<RegistryPublicationRecord> { pub });
        
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => _store.SaveAsync(path, model, cts.Token));
        Assert.False(File.Exists(path));
    }
}
