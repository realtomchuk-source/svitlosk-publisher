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
            "inmemory_territory",
            "inmemory_payload"
        ));
    }
}
