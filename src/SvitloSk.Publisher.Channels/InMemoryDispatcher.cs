using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Channels;

public class InMemoryDispatcher : IPublicationPort
{
    private readonly List<TransportArtifact> _artifacts = new();

    public IReadOnlyCollection<TransportArtifact> DispatchedArtifacts => _artifacts.AsReadOnly();

    public Task<AcceptedPublication> PublishAsync(TransportArtifact artifact, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Dispatching publication artifact: {artifact.RequestId}");
        _artifacts.Add(artifact);
        return Task.FromResult(new AcceptedPublication(artifact.RequestId, "in-memory-channel"));
    }

    public void Clear() => _artifacts.Clear();
}
