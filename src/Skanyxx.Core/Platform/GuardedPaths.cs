using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Routes that are origin-guarded for every method and rate limited: the new-module APIs (cookie or bearer), identity,
/// and <c>/mcp</c> (per-agent secrets, D080). Unsafe methods are origin-checked everywhere
/// (<see cref="OriginGuardMiddleware"/>).
/// </summary>
public static class GuardedPaths
{
    private static readonly PathString[] Prefixes = ["/api/memory", "/api/tickets", "/api/sandboxes", "/api/identity", "/mcp"];

    // Every POST that checks a password or a bootstrap token, through the API or the Razor forms. Sign-out and refresh
    // are not here: they stay in the general API window, so a burst of sign-ins cannot stop anyone signing out.
    private static readonly PathString[] CredentialPaths =
        ["/api/identity/sign-in", "/api/identity/bootstrap", "/api/identity/unlock", "/Login", "/Setup"];

    public static bool Contains(PathString path) => Prefixes.Any(path.StartsWithSegments);

    /// <summary>Counted in their own, stricter window (<see cref="SkanyxxOptions.SignInRateLimit"/>).</summary>
    public static bool IsCredentialPost(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && CredentialPaths.Any(request.Path.StartsWithSegments);
}
