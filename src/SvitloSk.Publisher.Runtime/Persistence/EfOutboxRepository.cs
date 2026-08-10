using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public IReadOnlyCollection<OutboxMessage> ClaimMessages(int batchSize, string workerId, TimeSpan leaseDuration)
    {
        var now = DateTimeOffset.UtcNow;
        var leasedUntil = now.Add(leaseDuration);
        
        var sql = @"
            WITH CTE AS (
                SELECT ""OperationId"", ""PublicationId"", ""Status"", ""NextRetryAt"", ""CreatedAt"", ""LeasedUntil""
                FROM ""OutboxMessages""
                WHERE ""Status"" IN (0, 1)
            ),
            Earliest AS (
                SELECT ""PublicationId"", MIN(""CreatedAt"") as EarliestCreatedAt
                FROM CTE
                GROUP BY ""PublicationId""
            ),
            Eligible AS (
                SELECT o.""OperationId""
                FROM ""OutboxMessages"" o
                INNER JOIN Earliest e ON o.""PublicationId"" = e.""PublicationId"" AND o.""CreatedAt"" = e.EarliestCreatedAt
                WHERE (o.""Status"" = 0 OR (o.""Status"" = 1 AND o.""LeasedUntil"" < @now))
                  AND (o.""NextRetryAt"" IS NULL OR o.""NextRetryAt"" <= @now)
                ORDER BY o.""CreatedAt""
                LIMIT @batchSize
                FOR UPDATE SKIP LOCKED
            )
            UPDATE ""OutboxMessages"" o
            SET ""Status"" = 1,
                ""ClaimedBy"" = @workerId,
                ""LeasedUntil"" = @leasedUntil
            FROM Eligible e
            WHERE o.""OperationId"" = e.""OperationId""
            RETURNING o.*;
        ";

        return _dbContext.OutboxMessages.FromSqlRaw(sql, 
            new NpgsqlParameter("@now", now),
            new NpgsqlParameter("@batchSize", batchSize),
            new NpgsqlParameter("@workerId", workerId),
            new NpgsqlParameter("@leasedUntil", leasedUntil))
            .ToList();
    }

    public IReadOnlyCollection<OutboxMessage> GetPendingMessages(int batchSize)
    {
        // Fallback for tests if needed. For PostgreSQL tests, ClaimMessages is authoritative.
        var now = DateTimeOffset.UtcNow;
        return _dbContext.OutboxMessages
            .Where(m => m.Status == OutboxOperationStatus.Pending || (m.Status == OutboxOperationStatus.Processing && m.LeasedUntil < now))
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
