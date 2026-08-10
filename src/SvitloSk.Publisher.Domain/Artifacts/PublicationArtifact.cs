using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Domain.Artifacts;

public record PublicationArtifact(
    Guid PublicationId,
    string Territory,
    PublicationClassification Classification,
    string Content,
    IReadOnlyDictionary<string, string>? GraphicData = null
);
