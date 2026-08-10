using System;

namespace SvitloSk.Publisher.Tests;

public class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FakeTimeProvider(DateTimeOffset initialTime)
    {
        _now = initialTime;
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void SetUtcNow(DateTimeOffset now) => _now = now;
    
    public void Advance(TimeSpan delta) => _now += delta;
}
