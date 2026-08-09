using System.Linq;
using SvitloSk.Publisher.Runtime;

namespace SvitloSk.Publisher.Runtime.Persistence;

public class EfExternalPublicationIdentityResolver : IExternalPublicationIdentityResolver
{
    private readonly SvitloSkDbContext _dbContext;

    public EfExternalPublicationIdentityResolver(SvitloSkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void RecordExternalIdentity(string publicationId, string externalId)
    {
        var existing = _dbContext.ExternalIdentities
            .FirstOrDefault(e => e.PublicationId == publicationId);
            
        if (existing != null)
        {
            existing.ExternalId = externalId;
        }
        else
        {
            _dbContext.ExternalIdentities.Add(new ExternalPublicationIdentity
            {
                PublicationId = publicationId,
                ExternalId = externalId
            });
        }
        
        // Transitional transaction boundary per ADR-006 Phase 1 removed. Commit is managed by IUnitOfWork.
    }

    public void RemoveExternalIdentity(string publicationId)
    {
        var existing = _dbContext.ExternalIdentities
            .FirstOrDefault(e => e.PublicationId == publicationId);
        if (existing != null)
        {
            _dbContext.ExternalIdentities.Remove(existing);
        }
    }

    public string? ResolveExternalIdentity(string publicationId)
    {
        return _dbContext.ExternalIdentities
            .FirstOrDefault(e => e.PublicationId == publicationId)?.ExternalId;
    }
}
