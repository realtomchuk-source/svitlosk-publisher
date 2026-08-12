using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Application.Interfaces;

public interface IGitTransport
{
    Task CommitAndPushAsync(string filePath, string commitMessage, CancellationToken cancellationToken = default);
    Task<string?> RestoreFromHistoryAsync(string filePath, CancellationToken cancellationToken = default);
}
