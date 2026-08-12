using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Application.Model;

public record InputTerritoryPackage(
    string TerritoryId,
    string Content,
    byte[]? GraphicBytes,
    bool IsPersistent
);

public record EditorialInput(
    string EditionDate,
    IReadOnlyList<InputTerritoryPackage> Packages,
    bool TomorrowForecastAvailable = false
);
