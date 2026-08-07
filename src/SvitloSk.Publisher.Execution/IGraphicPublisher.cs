using System.Collections.Generic;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public interface IGraphicPublisher
{
    IReadOnlyCollection<GraphicPublication> Publish(Edition edition, IReadOnlyCollection<PublicationArtifact> artifacts);
}
