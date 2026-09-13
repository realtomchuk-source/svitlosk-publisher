using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Domain;

/// <summary>
/// Domain model representing a journal header publication.
/// Platform-agnostic (contains clean business data without channel markup).
/// </summary>
public record JournalHeaderModel(
    string EditionDate,
    string TerritorialCommunityName,
    IReadOnlyList<string> PlannedSettlements,
    IReadOnlyList<string> EmergencySettlements
);

/// <summary>
/// Domain model representing an outage publication for a specific territory/starostat.
/// </summary>
public record TerritoryOutageModel(
    string CanonicalName,
    IReadOnlyList<OutageBlockModel> EmergencyBlocks,
    IReadOnlyList<OutageBlockModel> PlannedBlocks,
    bool IsTomorrow = false,
    string? TomorrowDate = null
);

/// <summary>
/// Domain model representing an outage sub-block within a specific time interval.
/// </summary>
public record OutageBlockModel(
    string TimeInterval,
    IReadOnlyList<string> DetailLines
);

/// <summary>
/// Domain model representing technical monitoring status publication (tail invariant).
/// </summary>
public record SystemStatusModel(
    DateTime LastUpdatedUtc,
    string MonitoringState = "активний"
);

/// <summary>
/// Domain model representing 12-subqueue graphic schedule.
/// </summary>
public record GraphicScheduleModel(
    string ScheduleDate,
    string TerritorialScope,
    byte[] SvgBytes,
    byte[]? PngBytes = null
);
