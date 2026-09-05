using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Engine;

public record OutageRecord(
    string TerritoryName,
    string OutageType, // "ПЛАНОВІ" or "АВАРІЙНІ"
    string Details,
    string? Queues = null
);

public interface IOutageFeedParser
{
    IReadOnlyList<OutageRecord> Parse(string rawFeed);
    GraphicInputPackage ParseGraphicSchedule(string rawFeed, string editionDate, string territorialScope = "Старокостянтинівська МТГ");
    GraphicInputPackage ParseLegacyGraphicJson(string legacyJson, string territorialScope = "Старокостянтинівська МТГ");
}


