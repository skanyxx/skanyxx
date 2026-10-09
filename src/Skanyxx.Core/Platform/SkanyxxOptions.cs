using System.ComponentModel.DataAnnotations;

namespace Skanyxx.Core.Platform;

/// <summary>Host-wide guards for the new-module APIs (<c>/api/memory|tickets|sandboxes|identity</c>, <c>/mcp</c>) and <c>/health</c>.</summary>
public sealed class SkanyxxOptions
{
    public const string Section = "Skanyxx";

    /// <summary>
    /// Browser origins allowed to call the guarded APIs, and to send unsafe methods anywhere, besides the origin of
    /// <c>Identity:PublicBaseUrl</c>. Neither set: a guarded route refuses any request carrying <c>Origin</c>; elsewhere
    /// only the page's own origin may POST/PUT/PATCH/DELETE.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    [Range(1, 60)]
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    public ApiRateLimitOptions RateLimit { get; set; } = new();

    /// <summary>Per-client window over sign-in, bootstrap and unlock (API and Razor forms); account lockout sits behind it.</summary>
    public ApiRateLimitOptions SignInRateLimit { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };

    /// <summary>
    /// Per-client window over the one-time links: the invite accept and password-reset pages (GET and POST) and the
    /// anonymous invite and reset lookup/accept API. Its own window, so link unfurlers and crawlers opening emailed links
    /// cannot spend the one sign-in depends on.
    /// </summary>
    public ApiRateLimitOptions InviteRateLimit { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}
