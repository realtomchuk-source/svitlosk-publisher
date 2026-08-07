using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SvitloSk.Publisher.Domain.Contracts;

public class GraphicMetadataDto
{
    [JsonPropertyName("package_id")]
    [JsonRequired]
    public Guid PackageId { get; set; }

    [JsonPropertyName("generation_timestamp")]
    [JsonRequired]
    public DateTimeOffset GenerationTimestamp { get; set; }

    [JsonPropertyName("target_date")]
    [JsonRequired]
    public DateOnly TargetDate { get; set; }

    [JsonPropertyName("source_identifier")]
    [JsonRequired]
    public string SourceIdentifier { get; set; } = string.Empty;
}

public class GraphicIntervalDto
{
    [JsonPropertyName("start_time")]
    [JsonRequired]
    public string StartTime { get; set; } = string.Empty;

    [JsonPropertyName("end_time")]
    [JsonRequired]
    public string EndTime { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    [JsonRequired]
    public string Status { get; set; } = string.Empty;
}

public class GraphicSubqueueDto
{
    [JsonPropertyName("subqueue_id")]
    [JsonRequired]
    public string SubqueueId { get; set; } = string.Empty;

    [JsonPropertyName("intervals")]
    [JsonRequired]
    public List<GraphicIntervalDto> Intervals { get; set; } = new();
}

public class GraphicQueueDto
{
    [JsonPropertyName("queue_id")]
    [JsonRequired]
    public string QueueId { get; set; } = string.Empty;

    [JsonPropertyName("subqueues")]
    [JsonRequired]
    public List<GraphicSubqueueDto> Subqueues { get; set; } = new();
}

public class GraphicInputPackageDto
{
    [JsonPropertyName("metadata")]
    [JsonRequired]
    public GraphicMetadataDto Metadata { get; set; } = new();

    [JsonPropertyName("territorial_scope")]
    [JsonRequired]
    public string TerritorialScope { get; set; } = string.Empty;

    [JsonPropertyName("queues")]
    [JsonRequired]
    public List<GraphicQueueDto> Queues { get; set; } = new();
}
