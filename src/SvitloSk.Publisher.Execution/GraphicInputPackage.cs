// Source: INPUT_PACKAGE_SPECIFICATION.md
// Section: 3.2

using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Execution;

public record GraphicInputPackage(
    GraphicInputMetadata Metadata,
    string TerritorialScope,
    IReadOnlyList<GraphicQueue> Queues
);

public record GraphicInputMetadata(
    Guid PackageId,
    string GenerationTimestamp,
    string TargetDate,
    string SourceIdentifier
);

public record GraphicQueue(
    string QueueId,
    IReadOnlyList<GraphicSubqueue> Subqueues
);

public record GraphicSubqueue(
    string SubqueueId,
    IReadOnlyList<GraphicInterval> Intervals
);

public record GraphicInterval(
    string StartTime,
    string EndTime,
    string Status
);
