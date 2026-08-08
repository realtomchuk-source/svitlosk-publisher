using System;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Runtime;

public class InMemoryEditionRepository : IEditionRepository
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Edition> _editions = new();

    public Edition? GetByDate(DateOnly targetDate)
    {
        foreach (var edition in _editions.Values)
        {
            if (edition.TargetDate == targetDate)
                return edition;
        }
        return null;
    }

    public Edition? GetById(Guid id)
    {
        _editions.TryGetValue(id, out var edition);
        return edition;
    }

    public void Save(Edition edition)
    {
        _editions[edition.Id] = edition;
    }
}

public class InMemoryInputPackageProvider : IInputPackageProvider
{
    public Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new InputPackage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "inmemory_source",
            "Starokostiantyniv Urban Territorial Community",
            new[] { new TerritorialPayload("inmemory_territory", SourcePortion.Today, "inmemory_payload") }
        ));
    }
}

public class InMemoryExternalPublicationIdentityResolver : IExternalPublicationIdentityResolver
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _identities = new();

    public void RecordExternalIdentity(string publicationId, string externalId)
    {
        _identities[publicationId] = externalId;
    }

    public string? ResolveExternalIdentity(string publicationId)
    {
        _identities.TryGetValue(publicationId, out var id);
        return id;
    }
}
