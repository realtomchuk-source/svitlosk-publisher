using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Model;

namespace SvitloSk.Publisher.Application.Interfaces;

public interface IPublisherOrchestrator
{
    Task<BatchDispatchResult> RunOrchestrationAsync(
        string registryPath,
        string chatNameOrId,
        EditorialInput input,
        CancellationToken cancellationToken = default);
}
