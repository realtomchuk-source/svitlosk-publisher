using System;

namespace SvitloSk.Publisher.Domain.Artifacts;

public record PublicationArtifact(
    Guid PublicationId,
    string Territory,
    PublicationClassification Classification,
    string Content
);
