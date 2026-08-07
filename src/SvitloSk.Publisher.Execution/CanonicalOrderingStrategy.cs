using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public class CanonicalOrderingStrategy : IEditorialOrderingStrategy
{
    public IEnumerable<string> Order(Edition edition, IEnumerable<PublicationArtifact> publications)
    {
        var ordered = new List<string>();
        var pubDict = publications.ToDictionary(p => p.PublicationId);

        foreach (var package in edition.Packages.OrderBy(p => p.Name))
        {
            // 1. Position 1: Tomorrow
            var tomorrowPub = package.Publications.FirstOrDefault(p => p.Type == PublicationType.Tomorrow);
            if (tomorrowPub != null && pubDict.TryGetValue(tomorrowPub.Id, out var tomorrowArt))
            {
                ordered.Add(tomorrowArt.Content);
            }

            // 2. Position 2: Starokostiantyniv
            var staroPubs = package.Publications.Where(p => p.TerritoryId == "Starokostiantyniv");
            foreach (var p in staroPubs)
            {
                if (pubDict.TryGetValue(p.Id, out var art)) ordered.Add(art.Content);
            }

            // 3. Position 3: Starostyn Districts (Alphabetical)
            var districtPubs = package.Publications
                .Where(p => p.TerritoryId != "Starokostiantyniv" && p.Type != PublicationType.Tomorrow && p.Type != PublicationType.Technical)
                .OrderBy(p => p.TerritoryId);
            foreach (var p in districtPubs)
            {
                if (pubDict.TryGetValue(p.Id, out var art)) ordered.Add(art.Content);
            }

            // 4. Position 4: Technical Publications
            var techPubs = package.Publications.Where(p => p.Type == PublicationType.Technical);
            foreach (var p in techPubs)
            {
                if (pubDict.TryGetValue(p.Id, out var art)) ordered.Add(art.Content);
            }
        }

        return ordered;
    }
}
