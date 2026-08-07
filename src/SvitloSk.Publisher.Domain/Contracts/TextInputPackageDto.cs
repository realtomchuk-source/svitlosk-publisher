using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SvitloSk.Publisher.Domain.Contracts;

public class TextMetadataDto
{
    [JsonPropertyName("package_id")]
    [JsonRequired]
    public Guid PackageId { get; set; }

    [JsonPropertyName("generation_timestamp")]
    [JsonRequired]
    public DateTimeOffset GenerationTimestamp { get; set; }

    [JsonPropertyName("source_identifier")]
    [JsonRequired]
    public string SourceIdentifier { get; set; } = string.Empty;

    [JsonPropertyName("package_state")]
    [JsonRequired]
    public PackageState PackageState { get; set; }
}

public class TextIntervalDto
{
    [JsonPropertyName("start_time")]
    [JsonRequired]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("end_time")]
    [JsonRequired]
    public DateTimeOffset EndTime { get; set; }
}

public class TextEventDto
{
    [JsonPropertyName("settlement")]
    [JsonRequired]
    public string Settlement { get; set; } = string.Empty;

    [JsonPropertyName("streets")]
    [JsonRequired]
    public List<string> Streets { get; set; } = new();

    [JsonPropertyName("intervals")]
    [JsonRequired]
    public List<TextIntervalDto> Intervals { get; set; } = new();
}

public class TextInputPackageDto
{
    [JsonPropertyName("metadata")]
    [JsonRequired]
    public TextMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("territorial_scope")]
    [JsonRequired]
    public string TerritorialScope { get; set; } = string.Empty;

    [JsonPropertyName("events")]
    [JsonRequired]
    public List<TextEventDto> Events { get; set; } = new();
}
