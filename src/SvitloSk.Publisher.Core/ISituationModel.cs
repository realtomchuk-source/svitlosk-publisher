using System;
using System.Collections.Generic;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Core;

public record InfrastructureState(bool IsProducerUnavailable, bool IsGraphicUnavailable, bool IsCommentFlood);

public interface ISituationModel
{
    IReadOnlyCollection<DetectedSituation> Detect(
        Edition currentEdition,
        InputPackage inputPackage,
        DateTimeOffset currentTime,
        TimeSpan cleanupThreshold,
        TimeSpan closeThreshold,
        InfrastructureState infraState);
}
