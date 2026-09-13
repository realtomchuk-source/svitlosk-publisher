using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SvitloSk.Publisher.Application.Model;

[JsonConverter(typeof(RegistryPublicationRecordConverter))]
public record RegistryPublicationRecord(
    Guid PublisherArtifactId,
    string TerritoryId,
    string? ExternalMessageId,
    string ContentHash,
    string TransmissionState,
    string PublicationType = "Text"
)
{
    public RegistryPublicationRecord(
        Guid publisherArtifactId,
        string territoryId,
        int telegramMessageId,
        string contentHash,
        string transmissionState,
        string publicationType = "Text"
    ) : this(publisherArtifactId, territoryId, telegramMessageId.ToString(), contentHash, transmissionState, publicationType)
    {
    }

    [JsonIgnore]
    public int? TelegramMessageId => int.TryParse(ExternalMessageId, out int id) ? id : null;
}

public class RegistryPublicationRecordConverter : JsonConverter<RegistryPublicationRecord>
{
    public override RegistryPublicationRecord Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected StartObject token when deserializing RegistryPublicationRecord.");

        Guid artifactId = Guid.Empty;
        string territoryId = string.Empty;
        string? externalMessageId = null;
        string contentHash = string.Empty;
        string transmissionState = string.Empty;
        string publicationType = "Text";

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propName = reader.GetString() ?? string.Empty;
                reader.Read();

                if (propName.Equals("publisher_artifact_id", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.String && Guid.TryParse(reader.GetString(), out var g))
                        artifactId = g;
                }
                else if (propName.Equals("territory_id", StringComparison.OrdinalIgnoreCase))
                {
                    territoryId = reader.GetString() ?? string.Empty;
                }
                else if (propName.Equals("external_message_id", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType == JsonTokenType.Number)
                        externalMessageId = reader.GetInt64().ToString();
                    else if (reader.TokenType == JsonTokenType.String)
                        externalMessageId = reader.GetString();
                }
                else if (propName.Equals("telegram_message_id", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(externalMessageId))
                    {
                        if (reader.TokenType == JsonTokenType.Number)
                            externalMessageId = reader.GetInt64().ToString();
                        else if (reader.TokenType == JsonTokenType.String)
                            externalMessageId = reader.GetString();
                    }
                }
                else if (propName.Equals("content_hash", StringComparison.OrdinalIgnoreCase))
                {
                    contentHash = reader.GetString() ?? string.Empty;
                }
                else if (propName.Equals("transmission_state", StringComparison.OrdinalIgnoreCase))
                {
                    transmissionState = reader.GetString() ?? string.Empty;
                }
                else if (propName.Equals("publication_type", StringComparison.OrdinalIgnoreCase))
                {
                    publicationType = reader.GetString() ?? "Text";
                }
            }
        }

        return new RegistryPublicationRecord(artifactId, territoryId, externalMessageId, contentHash, transmissionState, publicationType);
    }

    public override void Write(Utf8JsonWriter writer, RegistryPublicationRecord value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("publisher_artifact_id", value.PublisherArtifactId);
        writer.WriteString("territory_id", value.TerritoryId);

        if (value.ExternalMessageId != null)
        {
            writer.WriteString("external_message_id", value.ExternalMessageId);
            if (value.TelegramMessageId.HasValue)
            {
                writer.WriteNumber("telegram_message_id", value.TelegramMessageId.Value);
            }
        }
        else
        {
            writer.WriteNull("external_message_id");
            writer.WriteNull("telegram_message_id");
        }

        writer.WriteString("content_hash", value.ContentHash);
        writer.WriteString("transmission_state", value.TransmissionState);
        writer.WriteString("publication_type", value.PublicationType);
        writer.WriteEndObject();
    }
}

public record RegistryModel(
    int SchemaVersion,
    string EditionDate,
    string Status,
    IReadOnlyList<RegistryPublicationRecord> Publications
);

