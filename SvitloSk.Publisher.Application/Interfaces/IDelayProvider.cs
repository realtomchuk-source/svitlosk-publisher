using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Application.Interfaces;

public interface IDelayProvider
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}
