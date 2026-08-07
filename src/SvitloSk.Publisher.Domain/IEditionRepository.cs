using System;

namespace SvitloSk.Publisher.Domain;

public interface IEditionRepository
{
    Edition? GetById(Guid id);
    Edition? GetByDate(DateOnly targetDate);
    void Save(Edition edition);
}
