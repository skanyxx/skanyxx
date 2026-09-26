using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Core.Platform;

/// <summary>Host-wide guards for the header-identity APIs (<c>/api/memory|tickets|sandboxes</c>, <c>/mcp</c>) and <c>/health</c>.</summary>
public sealed class SkanyxxOptions
{
    public const string Section = "Skanyxx";

    /// <summary>Browser origins allowed to call the guarded APIs. Empty: any request carrying <c>Origin</c> is refused.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    [Range(1, 60)]
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    public ApiRateLimitOptions RateLimit { get; set; } = new();
}
