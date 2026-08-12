using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;

namespace SvitloSk.Publisher.Application.Interfaces;

public interface IRegistryStore
{
    Task<RegistryModel?> LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string path, RegistryModel model, CancellationToken cancellationToken = default);
}
