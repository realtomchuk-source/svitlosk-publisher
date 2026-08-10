using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SvitloSk.Publisher.Runtime.Persistence;
using SvitloSk.Publisher.Channels;
using System.Threading;

namespace SvitloSk.Publisher.Tests;

[Collection("PostgresCollection")]
public class OutboxConcurrencyTests
{
    private readonly PostgresTestFixture _fixture;

    public OutboxConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_TwoIndependentWorkers_AttemptToClaimConcurrently_OnlyOneSucceeds()
    {
        var publicationId = Guid.NewGuid().ToString();
        var message = new OutboxMessage
        {
            OperationId = Guid.NewGuid(),
            PublicationId = publicationId,
            OperationType = TransportOperation.CREATE,
            ArtifactType = TransportArtifactType.TEXT_ONLY,
            Status = OutboxOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Arrange
        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.Add(message);
            await setupContext.SaveChangesAsync();
        }

        // Act
        var worker1Id = "worker-1";
        var worker2Id = "worker-2";
        
        var task1 = Task.Run(() => 
        {
            using var context = new SvitloSkDbContext(_fixture.GetDbContextOptions());
            var repo = new EfOutboxRepository(context);
            // We use a larger batch size but we only have 1 message for this publication
            return repo.ClaimMessages(5, worker1Id, TimeSpan.FromMinutes(5));
        });

        var task2 = Task.Run(() => 
        {
            using var context = new SvitloSkDbContext(_fixture.GetDbContextOptions());
            var repo = new EfOutboxRepository(context);
            return repo.ClaimMessages(5, worker2Id, TimeSpan.FromMinutes(5));
        });

        var results = await Task.WhenAll(task1, task2);
        var claim1 = results[0].FirstOrDefault(m => m.PublicationId == publicationId);
        var claim2 = results[1].FirstOrDefault(m => m.PublicationId == publicationId);

        // Assert
        Assert.True((claim1 != null && claim2 == null) || (claim1 == null && claim2 != null), "Only one worker should claim the message");

        using (var verifyContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var dbMsg = await verifyContext.OutboxMessages.FindAsync(message.OperationId);
            Assert.NotNull(dbMsg);
            Assert.Equal(OutboxOperationStatus.Processing, dbMsg.Status);
            Assert.NotNull(dbMsg.ClaimedBy);
            
            if (claim1 != null) Assert.Equal(worker1Id, dbMsg.ClaimedBy);
            else Assert.Equal(worker2Id, dbMsg.ClaimedBy);
        }
    }

    [Fact]
    public async Task B_MultiplePendingMessages_DifferentPublicationIds_ClaimedIndependently()
    {
        var pub1 = Guid.NewGuid().ToString();
        var pub2 = Guid.NewGuid().ToString();
        
        var msg1 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pub1,
            OperationType = TransportOperation.CREATE, ArtifactType = TransportArtifactType.TEXT_ONLY,
            Status = OutboxOperationStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
        };
        var msg2 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pub2,
            OperationType = TransportOperation.CREATE, ArtifactType = TransportArtifactType.TEXT_ONLY,
            Status = OutboxOperationStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
        };

        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.AddRange(msg1, msg2);
            await setupContext.SaveChangesAsync();
        }

        using (var context = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var repo = new EfOutboxRepository(context);
            var claimed = repo.ClaimMessages(10, "worker-1", TimeSpan.FromMinutes(5));
            
            Assert.Contains(claimed, c => c.PublicationId == pub1);
            Assert.Contains(claimed, c => c.PublicationId == pub2);
        }
    }

    [Fact]
    public async Task C_MessagesBelongingToSamePublicationId_PreserveOrdering()
    {
        var pubId = Guid.NewGuid().ToString();
        
        var msg1 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pubId,
            OperationType = TransportOperation.CREATE, Status = OutboxOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        };
        var msg2 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pubId,
            OperationType = TransportOperation.UPDATE, Status = OutboxOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };

        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.AddRange(msg2, msg1); // Add in reverse order
            await setupContext.SaveChangesAsync();
        }

        using (var context = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var repo = new EfOutboxRepository(context);
            var claimed = repo.ClaimMessages(5, "worker-1", TimeSpan.FromMinutes(5));
            
            var pubClaim = claimed.Where(c => c.PublicationId == pubId).ToList();
            Assert.Single(pubClaim); // Only one per publication ID at a time
            Assert.Equal(msg1.OperationId, pubClaim[0].OperationId); // Must be the older one
        }
    }

    [Fact]
    public async Task D_DelayedFirstOperation_PreventsLaterOperation_FromBeingClaimed()
    {
        var pubId = Guid.NewGuid().ToString();
        
        var msg1 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pubId,
            OperationType = TransportOperation.CREATE, Status = OutboxOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            NextRetryAt = DateTimeOffset.UtcNow.AddMinutes(10) // Delayed
        };
        var msg2 = new OutboxMessage
        {
            OperationId = Guid.NewGuid(), PublicationId = pubId,
            OperationType = TransportOperation.UPDATE, Status = OutboxOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            NextRetryAt = null
        };

        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.AddRange(msg1, msg2);
            await setupContext.SaveChangesAsync();
        }

        using (var context = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var repo = new EfOutboxRepository(context);
            var claimed = repo.ClaimMessages(5, "worker-1", TimeSpan.FromMinutes(5));
            
            // Because msg1 is earliest but is delayed, NOTHING for this publication should be claimed
            var pubClaim = claimed.Where(c => c.PublicationId == pubId).ToList();
            Assert.Empty(pubClaim); 
        }
    }

    [Fact]
    public async Task E_LeaseExpiration_AllowsProcessingMessageToBeClaimedAgain()
    {
        var pubId = Guid.NewGuid().ToString();
        var msgId = Guid.NewGuid();
        
        var msg1 = new OutboxMessage
        {
            OperationId = msgId, PublicationId = pubId,
            OperationType = TransportOperation.CREATE, Status = OutboxOperationStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LeasedUntil = DateTimeOffset.UtcNow.AddMinutes(-1), // Expired
            ClaimedBy = "old-worker"
        };

        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.Add(msg1);
            await setupContext.SaveChangesAsync();
        }

        using (var context = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var repo = new EfOutboxRepository(context);
            var claimed = repo.ClaimMessages(5, "new-worker", TimeSpan.FromMinutes(5));
            
            var pubClaim = claimed.FirstOrDefault(c => c.PublicationId == pubId);
            Assert.NotNull(pubClaim);
            Assert.Equal(msgId, pubClaim.OperationId);
        }
    }

    [Fact]
    public async Task F_ValidProcessingLease_IsNotSimultaneouslyClaimed()
    {
        var pubId = Guid.NewGuid().ToString();
        var msgId = Guid.NewGuid();
        
        var msg1 = new OutboxMessage
        {
            OperationId = msgId, PublicationId = pubId,
            OperationType = TransportOperation.CREATE, Status = OutboxOperationStatus.Processing,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            LeasedUntil = DateTimeOffset.UtcNow.AddMinutes(10), // Not Expired
            ClaimedBy = "old-worker"
        };

        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            setupContext.OutboxMessages.Add(msg1);
            await setupContext.SaveChangesAsync();
        }

        using (var context = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            var repo = new EfOutboxRepository(context);
            var claimed = repo.ClaimMessages(5, "new-worker", TimeSpan.FromMinutes(5));
            
            var pubClaim = claimed.FirstOrDefault(c => c.PublicationId == pubId);
            Assert.Null(pubClaim);
        }
    }

    [Fact]
    public async Task G_ClaimingIsAtomic_WithHighConcurrency()
    {
        var publicationIds = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid().ToString()).ToList();
        
        using (var setupContext = new SvitloSkDbContext(_fixture.GetDbContextOptions()))
        {
            foreach (var pubId in publicationIds)
            {
                setupContext.OutboxMessages.Add(new OutboxMessage
                {
                    OperationId = Guid.NewGuid(), PublicationId = pubId,
                    OperationType = TransportOperation.CREATE, Status = OutboxOperationStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await setupContext.SaveChangesAsync();
        }

        int workerCount = 5;
        var tasks = new List<Task<IReadOnlyCollection<OutboxMessage>>>();
        
        for (int i = 0; i < workerCount; i++)
        {
            var workerId = $"worker-{i}";
            tasks.Add(Task.Run(() => 
            {
                using var context = new SvitloSkDbContext(_fixture.GetDbContextOptions());
                var repo = new EfOutboxRepository(context);
                return repo.ClaimMessages(2, workerId, TimeSpan.FromMinutes(5));
            }));
        }

        var results = await Task.WhenAll(tasks);
        
        // Ensure no overlapping claims
        var allClaimedIds = new HashSet<Guid>();
        foreach (var result in results)
        {
            foreach (var msg in result)
            {
                Assert.True(allClaimedIds.Add(msg.OperationId), $"Duplicate claim detected for OperationId {msg.OperationId}");
            }
        }
    }
}
