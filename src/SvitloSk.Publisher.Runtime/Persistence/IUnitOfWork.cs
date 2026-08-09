using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Runtime.Persistence;

public interface IUnitOfWork
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}
