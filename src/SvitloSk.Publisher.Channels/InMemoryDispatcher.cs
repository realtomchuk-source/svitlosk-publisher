using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Channels;

public class InMemoryDispatcher : IPublicationPort
{
    private readonly List<TransportArtifact> _artifacts = new();

    public IReadOnlyCollection<TransportArtifact> DispatchedArtifacts => _artifacts.AsReadOnly();

    public AcceptedPublication Publish(TransportArtifact artifact)
    {
        Console.WriteLine($"Dispatching publication artifact: {artifact.RequestId}");
        _artifacts.Add(artifact);
        return new AcceptedPublication(artifact.RequestId, "in-memory-channel");
    }

    public void Clear() => _artifacts.Clear();
}
