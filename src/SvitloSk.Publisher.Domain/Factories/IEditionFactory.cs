using System;

namespace SvitloSk.Publisher.Domain.Factories;

public interface IEditionFactory
{
    Edition Create(DateOnly targetDate);
}
