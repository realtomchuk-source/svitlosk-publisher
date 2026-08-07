using System;
using System.Collections.Generic;

namespace SvitloSk.Publisher.Channels;

public class InMemoryDispatcher : IPublicationPort
{
    private readonly List<PublicationRequest> _requests = new();

    public IReadOnlyCollection<PublicationRequest> DispatchedRequests => _requests.AsReadOnly();

    public AcceptedPublication Publish(PublicationRequest request)
    {
        Console.WriteLine($"Dispatching publication request: {request.Id}");
        _requests.Add(request);
        return new AcceptedPublication(request.Id, "in-memory-channel");
    }
}
