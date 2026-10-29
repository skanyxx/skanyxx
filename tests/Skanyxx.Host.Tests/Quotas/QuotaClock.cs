namespace Skanyxx.Host.Tests.Quotas;

/// <summary>A clock the test sets by hand, for month boundaries.</summary>
internal sealed class QuotaClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
}
