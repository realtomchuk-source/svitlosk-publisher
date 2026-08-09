using System;
using System.Collections.Generic;
using System.Linq;

namespace SvitloSk.Publisher.Runtime.Persistence;

public class EfOutboxRepository : IOutboxRepository
{
    private readonly SvitloSkDbContext _dbContext;

    public EfOutboxRepository(SvitloSkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(OutboxMessage message)
    {
        _dbContext.OutboxMessages.Add(message);
    }

    public IReadOnlyCollection<OutboxMessage> GetPendingMessages(int batchSize)
    {
        var now = DateTimeOffset.UtcNow;
        
        // Ensure strictly sequential processing per PublicationId.
        // We find the earliest pending message for each PublicationId.
        // If its NextRetryAt is > now, we skip processing for that PublicationId.
        return _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxOperationStatus.Pending)
            .GroupBy(m => m.PublicationId)
            .Select(g => g.OrderBy(m => m.CreatedAt).First())
            .Where(m => m.NextRetryAt == null || m.NextRetryAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(batchSize)
            .ToList();
    }

    public OutboxMessage? GetById(Guid id)
    {
        return _dbContext.OutboxMessages.FirstOrDefault(m => m.OperationId == id);
    }

    public IEnumerable<OutboxMessage> GetAll()
    {
        return _dbContext.OutboxMessages.ToList();
    }
}
