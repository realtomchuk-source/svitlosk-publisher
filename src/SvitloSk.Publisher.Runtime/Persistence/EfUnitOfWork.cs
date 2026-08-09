using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Runtime.Persistence;

public class EfUnitOfWork : IUnitOfWork
{
    private readonly SvitloSkDbContext _dbContext;

    public EfUnitOfWork(SvitloSkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
