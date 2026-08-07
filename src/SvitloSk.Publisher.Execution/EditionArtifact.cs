using System.Collections.Generic;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public record EditionArtifact(
    IReadOnlyList<PublicationArtifact> OrderedPublications
);
