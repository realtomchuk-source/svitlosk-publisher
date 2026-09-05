using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Core.Engine;

public enum TerritoryType
{
    CITY,
    STAROSTY_DISTRICT
}

public record CanonicalTerritory(
    string TerritoryId,
    string CanonicalName,
    TerritoryType Type,
    string CenterSettlement,
    IReadOnlyList<string> Settlements,
    IReadOnlyList<string> Aliases
);

public class TerritoryRegistry
{
    private static readonly List<CanonicalTerritory> _territories = new()
    {
        new CanonicalTerritory(
            "starokostiantyniv",
            "Місто Старокостянтинів",
            TerritoryType.CITY,
            "Старокостянтинів",
            new[] { "Старокостянтинів" },
            new[] { "м. Старокостянтинів", "Старокостянтинів", "starokostiantyniv" }
        ),
        new CanonicalTerritory("baglaivskyi", "Баглаївський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Баглаї", Array.Empty<string>(), new[] { "Баглаївський СО", "Баглаївський старостинський округ" }),
        new CanonicalTerritory("bereznenskyi", "Березненський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Березне", Array.Empty<string>(), new[] { "Березненський СО", "Березненський старостинський округ" }),
        new CanonicalTerritory("velykomatsevytskyi", "Великомацевицький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Великі Мацевичі", Array.Empty<string>(), new[] { "Великомацевицький СО", "Великомацевицький старостинський округ" }),
        new CanonicalTerritory("velykоcherniatynskyi", "Великочернятинський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Великий Чернятин", Array.Empty<string>(), new[] { "Великочернятинський СО", "Великочернятинський старостинський округ" }),
        new CanonicalTerritory("verborodynskyi", "Вербородинський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Вербородинці", Array.Empty<string>(), new[] { "Вербородинський СО", "Вербородинський старостинський округ" }),
        new CanonicalTerritory("vesnianskyi", "Веснянський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Веснянка", Array.Empty<string>(), new[] { "Веснянський СО", "Веснянський старостинський округ" }),
        new CanonicalTerritory("volytse-kereкeshynskyi", "Волице-Керекешинський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Волиця-Керекешина", Array.Empty<string>(), new[] { "Волице-Керекешинський СО", "Волице-Керекешинський старостинський округ" }),
        new CanonicalTerritory("voronkivskyi", "Воронківський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Воронківці", Array.Empty<string>(), new[] { "Воронківський СО", "Воронківський старостинський округ" }),
        new CanonicalTerritory("hryhorivskyi", "Григорівський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Григорівка", Array.Empty<string>(), new[] { "Григорівський СО", "Григорівський старостинський округ" }),
        new CanonicalTerritory("hubchanskyi", "Губчанський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Губча", Array.Empty<string>(), new[] { "Губчанський СО", "Губчанський старостинський округ" }),
        new CanonicalTerritory("irshykivskyi", "Іршиківський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Іршики", Array.Empty<string>(), new[] { "Іршиківський СО", "Іршиківський старостинський округ" }),
        new CanonicalTerritory("kapustynskyi", "Капустинський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Капустин", Array.Empty<string>(), new[] { "Капустинський СО", "Капустинський старостинський округ" }),
        new CanonicalTerritory("ohiivskyi", "Огіївський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Огіївка", Array.Empty<string>(), new[] { "Огіївський СО", "Огіївський старостинський округ" }),
        new CanonicalTerritory("pashkovetskyi", "Пашковецький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Пашківці", Array.Empty<string>(), new[] { "Пашковецький СО", "Пашковецький старостинський округ" }),
        new CanonicalTerritory("penkivskyi", "Пеньківський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Пеньки", Array.Empty<string>(), new[] { "Пеньківський СО", "Пеньківський старостинський округ" }),
        new CanonicalTerritory("radkovetskyi", "Радковецький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Радківці", Array.Empty<string>(), new[] { "Радковецький СО", "Радковецький старостинський округ" }),
        new CanonicalTerritory("reshnivetskyi", "Решнівецький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Решнівка", Array.Empty<string>(), new[] { "Решнівецький СО", "Решнівецький старостинський округ" }),
        new CanonicalTerritory("rosolivetskyi", "Росолівецький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Росолівці", Array.Empty<string>(), new[] { "Росолівецький СО", "Росолівецький старостинський округ" }),
        new CanonicalTerritory("samchykivskyi", "Самчиківський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Самчики", Array.Empty<string>(), new[] { "Самчиківський СО", "Самчиківський старостинський округ", "Самчики", "Самчиківський старостинський округ" }),
        new CanonicalTerritory("sakhnovetskyi", "Сахновецький старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Сахнівці", Array.Empty<string>(), new[] { "Сахновецький СО", "Сахновецький старостинський округ" }),
        new CanonicalTerritory("stetskivskyi", "Стецьківський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Стецьки", Array.Empty<string>(), new[] { "Стецьківський СО", "Стецьківський старостинський округ", "Стецьки" }),
        new CanonicalTerritory("krasnosilskyi", "Красносільський старостинський округ", TerritoryType.STAROSTY_DISTRICT, "Красносілка", Array.Empty<string>(), new[] { "Красносільський СО", "Красносільський старостинський округ", "Красносілка" })
    };

    public static IReadOnlyList<CanonicalTerritory> Territories => _territories;

    public static string MapRawTerritoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Territory name cannot be null or empty.");

        // Check territoryId match first
        foreach (var t in _territories)
        {
            if (t.TerritoryId.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return t.TerritoryId;
            }
        }

        // Check canonicalName match
        foreach (var t in _territories)
        {
            if (t.CanonicalName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return t.TerritoryId;
            }
        }

        // Check aliases match
        foreach (var t in _territories)
        {
            foreach (var alias in t.Aliases)
            {
                if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return t.TerritoryId;
                }
            }
        }

        throw new InvalidOperationException($"Territory '{name}' could not be resolved to a canonical routing mapping.");
    }
}
