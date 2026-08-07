// Source: EDITION_ASSEMBLY_SPECIFICATION.md
// Section: 01

using System.Collections.Generic;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public interface IEditionAssembly
{
    EditionArtifact Assemble(Edition edition, IEnumerable<PublicationArtifact> publications);
}
