using System;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Domain;

namespace SvitloSk.Publisher.Runtime;

public class DummyEditionRepository : IEditionRepository
{
    public Edition GetByDate(DateOnly targetDate) => null;
    public Edition GetById(Guid id) => null;
    public void Save(Edition edition) {}
}

public class DummyInputPackageProvider : IInputPackageProvider
{
    public Task<InputPackage> GetLatestAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new InputPackage(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "dummy_source",
            "dummy_territory",
            "dummy_payload"
        ));
    }
}
