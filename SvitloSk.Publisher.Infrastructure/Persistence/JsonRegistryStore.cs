using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;
using SvitloSk.Publisher.Application.Model;

namespace SvitloSk.Publisher.Infrastructure.Persistence;

public class JsonRegistryStore : IRegistryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNameCaseInsensitive = true
    };

    private readonly FileSystemAtomicWriter _atomicWriter;

    public JsonRegistryStore(FileSystemAtomicWriter atomicWriter)
    {
        _atomicWriter = atomicWriter ?? throw new ArgumentNullException(nameof(atomicWriter));
    }

    public async Task<RegistryModel?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var model = await JsonSerializer.DeserializeAsync<RegistryModel>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);

            if (model == null)
                throw new InvalidOperationException("Deserialization returned null.");

            ValidateModel(model);
            return model;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException($"Failed to load or parse registry from '{path}'.", ex);
        }
    }

    public async Task SaveAsync(string path, RegistryModel model, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

        if (model == null)
            throw new ArgumentNullException(nameof(model));

        ValidateModel(model);

        string json = JsonSerializer.Serialize(model, SerializerOptions);
        await _atomicWriter.WriteAtomicAsync(path, json, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateModel(RegistryModel model)
    {
        if (model.SchemaVersion <= 0)
            throw new InvalidOperationException("Registry schema_version is invalid or missing.");

        if (string.IsNullOrWhiteSpace(model.EditionDate))
            throw new InvalidOperationException("Registry edition_date is missing.");

        if (string.IsNullOrWhiteSpace(model.Status))
            throw new InvalidOperationException("Registry status is missing.");

        if (model.Publications == null)
            throw new InvalidOperationException("Registry publications collection is missing.");

        foreach (var pub in model.Publications)
        {
            if (pub.PublisherArtifactId == Guid.Empty)
                throw new InvalidOperationException("Publication record has an empty publisher_artifact_id.");

            if (string.IsNullOrWhiteSpace(pub.TerritoryId))
                throw new InvalidOperationException("Publication record has a missing territory_id.");

            if (string.IsNullOrWhiteSpace(pub.ContentHash))
                throw new InvalidOperationException("Publication record has a missing content_hash.");

            if (string.IsNullOrWhiteSpace(pub.TransmissionState))
                throw new InvalidOperationException("Publication record has a missing transmission_state.");

            // SENT and UPDATED records must have telegram_message_id
            string state = pub.TransmissionState.ToUpperInvariant();
            if ((state == "SENT" || state == "UPDATED") && pub.TelegramMessageId == null)
            {
                throw new InvalidOperationException($"Publication in state '{pub.TransmissionState}' must possess a telegram_message_id.");
            }
        }
    }
}
