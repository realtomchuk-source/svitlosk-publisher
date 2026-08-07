// Source: EDITION_ASSEMBLY_SPECIFICATION.md
// Section: 02

using System;
using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public class EditionAssembly : IEditionAssembly
{
    public EditionArtifact Assemble(
        IEnumerable<EditorialDecision> decisions,
        EditionState currentState,
        IEnumerable<PublicationArtifact> publications,
        IEnumerable<PackageArtifact> packages)
    {
        if (decisions == null) throw new ArgumentNullException(nameof(decisions));
        if (publications == null) throw new ArgumentNullException(nameof(publications));
        if (packages == null) throw new ArgumentNullException(nameof(packages));

        var ordered = new List<string>();

        var tomorrowPackage = packages.FirstOrDefault(p => p.PackageType == "Tomorrow");
        var technicalPackage = packages.FirstOrDefault(p => p.PackageType == "Technical");

        // 1. Position 1: Tomorrow Graph
        if (tomorrowPackage != null && !string.IsNullOrEmpty(tomorrowPackage.GraphContent))
        {
            ordered.Add(tomorrowPackage.GraphContent);
        }

        // 2. Position 2: Starokostiantyniv
        var staro = publications.Where(p => p.Territory == "Starokostiantyniv");
        ordered.AddRange(staro.Select(p => p.Content));

        // 3. Position 3: Starostyn Districts (Alphabetical)
        var districts = publications
            .Where(p => p.Territory != "Starokostiantyniv")
            .OrderBy(p => p.Territory);
        ordered.AddRange(districts.Select(p => p.Content));

        // 4. Position 4: Technical Publications
        if (technicalPackage != null && !string.IsNullOrEmpty(technicalPackage.TechnicalContent))
        {
            ordered.Add(technicalPackage.TechnicalContent);
        }

        // 5. Position 5: Tomorrow Forecast
        if (tomorrowPackage != null && !string.IsNullOrEmpty(tomorrowPackage.ForecastContent))
        {
            ordered.Add(tomorrowPackage.ForecastContent);
        }

        return new EditionArtifact(ordered.AsReadOnly());
    }
}
