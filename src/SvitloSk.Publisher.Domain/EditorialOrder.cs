using System;
using System.Collections.Generic;
using System.Linq;

namespace SvitloSk.Publisher.Domain;

public class EditorialOrder
{
    public Guid EditionId { get; }
    public IReadOnlyList<PublicationPackage> OrderedPackages { get; }
    public IReadOnlyList<Guid> OrderedPublicationIds { get; }

    public EditorialOrder(Edition edition)
    {
        EditionId = edition.Id;

        // Extract all publications from all packages in the edition
        var allPublications = edition.Packages.SelectMany(p => p.Publications).ToList();
        
        var orderedIds = new List<Guid>();

        // Position 1: Tomorrow
        var tomorrowPub = allPublications.FirstOrDefault(p => p.Type == PublicationType.Tomorrow);
        if (tomorrowPub != null) orderedIds.Add(tomorrowPub.Id);

        // Position 2: Starokostiantyniv
        var staroPubs = allPublications.Where(p => p.TerritoryId == "Starokostiantyniv").ToList();
        foreach (var p in staroPubs) orderedIds.Add(p.Id);

        // Position 3: Starostyn Districts (Alphabetical)
        var districtPubs = allPublications
            .Where(p => p.TerritoryId != "Starokostiantyniv" && p.Type != PublicationType.Tomorrow && p.Type != PublicationType.Technical)
            .OrderBy(p => p.TerritoryId)
            .ToList();
        foreach (var p in districtPubs) orderedIds.Add(p.Id);

        // Position 4: Technical Publications
        var techPubs = allPublications.Where(p => p.Type == PublicationType.Technical).ToList();
        foreach (var p in techPubs) orderedIds.Add(p.Id);

        OrderedPublicationIds = orderedIds.AsReadOnly();

        // Package ordering driven by EditorialOrder
        var orderedPkgs = new List<PublicationPackage>();
        foreach (var package in edition.Packages.OrderBy(p => p.Name))
        {
            // Re-order publications inside the package based on canonical order
            var sortedPubs = package.Publications
                .OrderBy(p => orderedIds.IndexOf(p.Id) == -1 ? int.MaxValue : orderedIds.IndexOf(p.Id))
                .ToList();
            
            var orderedPkg = new PublicationPackage(package.Id, package.Name);
            foreach (var p in sortedPubs)
            {
                orderedPkg.AddPublication(p);
            }
            orderedPkgs.Add(orderedPkg);
        }

        OrderedPackages = orderedPkgs.AsReadOnly();
    }
}
