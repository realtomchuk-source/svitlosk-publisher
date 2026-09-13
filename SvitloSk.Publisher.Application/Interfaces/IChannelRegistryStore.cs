using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;

namespace SvitloSk.Publisher.Application.Interfaces;

/// <summary>
/// Channel-specific registry storage port (Clean Architecture Port).
/// Each channel (Telegram, Facebook) owns its isolated state store.
/// </summary>
public interface IChannelRegistryStore
{
    string ChannelName { get; }
    string RegistryPath { get; }

    Task<RegistryModel?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(RegistryModel model, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);
}
