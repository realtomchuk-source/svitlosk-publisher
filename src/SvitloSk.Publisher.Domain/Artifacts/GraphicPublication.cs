using System;

namespace SvitloSk.Publisher.Domain.Artifacts;

public record GraphicPublication(
    Guid PublicationId,
    string Territory,
    PublicationClassification Classification,
    string GraphicContent
);
