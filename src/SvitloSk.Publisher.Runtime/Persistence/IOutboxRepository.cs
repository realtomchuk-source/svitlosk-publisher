using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Runtime.Persistence;

public interface IOutboxRepository
{
    void Add(OutboxMessage message);
    IReadOnlyCollection<OutboxMessage> GetPendingMessages(int batchSize);
    IEnumerable<OutboxMessage> GetAll();
    OutboxMessage? GetById(Guid id);
}
