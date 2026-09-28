using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Core.Platform;

/// <summary>Host-wide guards for the new-module APIs (<c>/api/memory|tickets|sandboxes|identity</c>, <c>/mcp</c>) and <c>/health</c>.</summary>
public sealed class SkanyxxOptions
{
    public const string Section = "Skanyxx";

    /// <summary>
    /// Browser origins allowed to call the guarded APIs, and to send unsafe methods anywhere. Empty: a guarded route
    /// refuses any request carrying <c>Origin</c>; elsewhere only the page's own origin may POST/PUT/PATCH/DELETE.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    [Range(1, 60)]
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    public ApiRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>Per-client window over sign-in, bootstrap and unlock (API and Razor forms); account lockout sits behind it.</summary>
    public ApiRateLimitOptions SignInRateLimit { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}
