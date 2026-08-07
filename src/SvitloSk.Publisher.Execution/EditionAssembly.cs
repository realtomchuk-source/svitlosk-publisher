// Source: EDITION_ASSEMBLY_SPECIFICATION.md
// Section: 02

using System;
using System.Collections.Generic;
using System.Linq;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public class EditionAssembly : IEditionAssembly
{
    private readonly IEditorialOrderingStrategy _orderingStrategy;

    public EditionAssembly(IEditorialOrderingStrategy orderingStrategy)
    {
        _orderingStrategy = orderingStrategy;
    }

    public EditionArtifact Assemble(Edition edition, IEnumerable<PublicationArtifact> publications)
    {
        if (edition == null) throw new ArgumentNullException(nameof(edition));
        if (publications == null) throw new ArgumentNullException(nameof(publications));

        var ordered = _orderingStrategy.Order(edition, publications);

        return new EditionArtifact(ordered.ToList().AsReadOnly());
    }
}
