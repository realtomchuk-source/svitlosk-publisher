using System;

namespace SvitloSk.Publisher.Runtime.Persistence;

public enum OutboxOperationStatus
{
    Pending,
    Processing,
    Completed,
    Failed
}

public class OutboxMessage
{
    public Guid OperationId { get; set; }
    public string PublicationId { get; set; } = null!;
    
    // We'll map enum TransportOperation from SvitloSk.Publisher.Channels
    public SvitloSk.Publisher.Channels.TransportOperation OperationType { get; set; }
    
    // We'll map enum TransportArtifactType from SvitloSk.Publisher.Channels
    public SvitloSk.Publisher.Channels.TransportArtifactType ArtifactType { get; set; }
    
    public string? ExternalIdentity { get; set; }
    public string? Payload { get; set; }
    
    public OutboxOperationStatus Status { get; set; }
    public int AttemptCount { get; set; }
    
    public DateTimeOffset? NextRetryAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? LeasedUntil { get; set; }
    public string? ClaimedBy { get; set; }
}
