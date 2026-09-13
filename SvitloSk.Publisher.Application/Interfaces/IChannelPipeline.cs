using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;
using SvitloSk.Publisher.Core.Engine;

namespace SvitloSk.Publisher.Application.Interfaces;

/// <summary>
/// Universal channel pipeline port (Clean Architecture Port).
/// Bridges platform-agnostic editorial decisions with platform-specific channel adapters.
/// </summary>
public interface IChannelPipeline
{
    string ChannelName { get; }

    Task<BatchDispatchResult> DispatchAsync(
        IReadOnlyList<EditorialDecision> decisions,
        CancellationToken cancellationToken = default
    );
}
