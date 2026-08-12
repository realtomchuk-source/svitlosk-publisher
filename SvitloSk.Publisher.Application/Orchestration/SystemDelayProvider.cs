using System;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Application.Orchestration;

public class SystemDelayProvider : Interfaces.IDelayProvider
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        return Task.Delay(milliseconds, cancellationToken);
    }
}
