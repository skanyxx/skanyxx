using Microsoft.AspNetCore.Http;

namespace Skanyxx.Core.Platform;

/// <summary>Routes whose caller identity is a plain header, so they must not be reachable from a web page.</summary>
public static class GuardedPaths
{
    private static readonly PathString[] Prefixes = ["/api/memory", "/api/tickets", "/api/sandboxes", "/mcp"];

    public static bool Contains(PathString path) => Prefixes.Any(path.StartsWithSegments);
}
