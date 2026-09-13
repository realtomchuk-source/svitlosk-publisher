using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Engine;

public record TransformedPackage(
    string TerritoryId,
    string Content,
    byte[]? GraphicBytes,
    bool IsPersistent
);

public record GraphicInterval(
    string StartTime,
    string EndTime,
    string Status
)
{
    public (TimeSpan Start, TimeSpan End) ParseTimes()
    {
        if (string.IsNullOrWhiteSpace(StartTime) || !TimeSpan.TryParse(StartTime, out var start))
            throw new FormatException($"Invalid StartTime format: '{StartTime}'. Expected HH:mm.");

        TimeSpan end;
        if (EndTime == "24:00")
        {
            end = TimeSpan.FromHours(24);
        }
        else if (string.IsNullOrWhiteSpace(EndTime) || !TimeSpan.TryParse(EndTime, out end))
        {
            throw new FormatException($"Invalid EndTime format: '{EndTime}'. Expected HH:mm.");
        }

        if (start < TimeSpan.Zero || start > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(StartTime), "StartTime must be between 00:00 and 24:00.");

        if (end < TimeSpan.Zero || end > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(EndTime), "EndTime must be between 00:00 and 24:00.");

        if (start >= end)
            throw new ArgumentException($"StartTime ({StartTime}) must be strictly earlier than EndTime ({EndTime}).");

        return (start, end);
    }
}

public record SubqueueSchedule(
    string SubqueueId,
    IReadOnlyList<GraphicInterval> Intervals
);

public record QueueSchedule(
    string QueueId,
    IReadOnlyList<SubqueueSchedule> Subqueues
);

public record GraphicMetadata(
    string PackageId,
    string GenerationTimestamp,
    string TargetDate,
    string SourceIdentifier
);

public record GraphicInputPackage(
    GraphicMetadata Metadata,
    string TerritorialScope,
    IReadOnlyList<QueueSchedule> Queues
);

public interface IGraphicRasterizer
{
    byte[] RasterizeSvgToPng(byte[] svgBytes, int width = 1000, int height = 650);
}

public interface IGraphicAssembly
{
    byte[] AssembleSvg(GraphicInputPackage inputPackage);
}
