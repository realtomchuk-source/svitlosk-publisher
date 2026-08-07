// Source: EDITION_ASSEMBLY_SPECIFICATION.md
// Section: 01

using System.Collections.Generic;

namespace SvitloSk.Publisher.Execution;

public record EditionArtifact(
    IReadOnlyList<string> OrderedContent
);
