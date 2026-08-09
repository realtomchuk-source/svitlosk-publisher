using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Runtime.Persistence;

public class EfEditionRepository : IEditionRepository
{
    private readonly SvitloSkDbContext _dbContext;

    public EfEditionRepository(SvitloSkDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Edition? GetById(Guid id)
    {
        return _dbContext.Editions
            .Include(e => e.Packages)
            .ThenInclude(p => p.Publications)
            .FirstOrDefault(e => e.Id == id);
    }

    public Edition? GetByDate(DateOnly targetDate)
    {
        return _dbContext.Editions
            .Include(e => e.Packages)
            .ThenInclude(p => p.Publications)
            .FirstOrDefault(e => e.TargetDate == targetDate);
    }

    public void Save(Edition edition)
    {
        var existing = _dbContext.Editions.Local.FirstOrDefault(e => e.Id == edition.Id);
        
        if (existing == null)
        {
            // It's a new edition or it was loaded with AsNoTracking (which we don't use here)
            // If it's not tracked locally, check database to be safe
            var dbTracked = _dbContext.Editions.Any(e => e.Id == edition.Id);
            if (!dbTracked)
            {
                _dbContext.Editions.Add(edition);
            }
            else
            {
                _dbContext.Editions.Update(edition);
            }
        }
        
        // Transitional boundary per ADR-006 Phase 1 removed. Commit is now managed by IUnitOfWork.
    }
}
