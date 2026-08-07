using System;

namespace SvitloSk.Publisher.Domain.Factories;

public class EditionFactory : IEditionFactory
{
    public Edition Create(DateOnly targetDate)
    {
        return new Edition(Guid.NewGuid(), targetDate);
    }
}
