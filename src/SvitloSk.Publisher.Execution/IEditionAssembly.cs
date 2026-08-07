// Source: EDITION_ASSEMBLY_SPECIFICATION.md
// Section: 01

using System.Collections.Generic;
using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public interface IEditionAssembly
{
    EditionArtifact Assemble(
        IEnumerable<EditorialDecision> decisions,
        EditionState currentState,
        IEnumerable<PublicationArtifact> publications,
        IEnumerable<PackageArtifact> packages);
}
