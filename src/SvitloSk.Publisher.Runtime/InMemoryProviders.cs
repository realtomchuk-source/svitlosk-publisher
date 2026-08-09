using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Domain;
using SvitloSk.Publisher.Runtime.Persistence;

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
            new[] { new Event("inmemory_territory", new string[0], new Interval[0]) }
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

    public void RemoveExternalIdentity(string publicationId)
    {
        _identities.Remove(publicationId, out _);
    }

    public string? ResolveExternalIdentity(string publicationId)
    {
        _identities.TryGetValue(publicationId, out var id);
        return id;
    }
}

public class InMemoryOutboxRepository : IOutboxRepository
{
    private readonly List<OutboxMessage> _messages = new();

    public void Add(OutboxMessage message)
    {
        _messages.Add(message);
    }

    public OutboxMessage? GetById(Guid id)
    {
        return _messages.FirstOrDefault(m => m.OperationId == id);
    }

    public IReadOnlyCollection<OutboxMessage> GetPendingMessages(int batchSize)
    {
        var now = DateTimeOffset.UtcNow;
        return _messages
            .Where(m => m.Status == OutboxOperationStatus.Pending)
            .GroupBy(m => m.PublicationId)
            .Select(g => g.OrderBy(m => m.CreatedAt).First())
            .Where(m => m.NextRetryAt == null || m.NextRetryAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToList();
    }

    public void Clear()
    {
        _messages.Clear();
    }

    public IEnumerable<OutboxMessage> GetAll() => _messages;
}

public class InMemoryUnitOfWork : IUnitOfWork
{
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
