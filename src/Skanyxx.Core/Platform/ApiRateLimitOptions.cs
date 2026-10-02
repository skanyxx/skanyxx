namespace Skanyxx.Core.Platform;

/// <summary>Fixed window per client (see <see cref="ClientPartition"/>) over the guarded APIs.</summary>
public sealed class ApiRateLimitOptions
{
    public int PermitLimit { get; set; } = 200;

    public int WindowSeconds { get; set; } = 10;
}
