// Source: SYNCHRONIZATION_ENGINE_SPECIFICATION.md
// Section: 4

using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Runtime;

public interface ISynchronizationEngine
{
    Task MaintainPublisherStateAsync(CancellationToken cancellationToken);
}
