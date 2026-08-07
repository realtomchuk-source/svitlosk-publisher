// Source: PUBLICATION_ASSEMBLY_SPECIFICATION.md
// Section: 01

using SvitloSk.Publisher.Core;

namespace SvitloSk.Publisher.Execution;

public record PublicationArtifact(
    string Territory,
    Classification Classification,
    string Content
);
