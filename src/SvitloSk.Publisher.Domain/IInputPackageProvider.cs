using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Domain;

public interface IInputPackageProvider
{
    Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken);
}
