namespace Skanyxx.Module.Identity.Tests.Infrastructure;

/// <summary>A clock the test moves by hand, for session caps measured in days.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
