using System.Collections.Generic;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Domain.Artifacts;

namespace SvitloSk.Publisher.Execution;

public interface IEditorialOrderingStrategy
{
    IEnumerable<PublicationArtifact> Order(Edition edition, IEnumerable<PublicationArtifact> publications);
}
