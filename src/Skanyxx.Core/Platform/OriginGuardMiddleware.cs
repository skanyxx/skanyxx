using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Refuses guarded routes to any browser origin not explicitly allowed. <c>DisableCors</c> only stops cross-origin
/// reads; a DNS-rebound page is same-origin and would otherwise set the identity headers freely.
/// Non-browser clients (kagent, curl, MCP SDKs) send no <c>Origin</c> and pass.
/// </summary>
public sealed class OriginGuardMiddleware(RequestDelegate next, IOptions<SkanyxxOptions> options)
{
    private readonly HashSet<string> _allowed = new(
        options.Value.AllowedOrigins.Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        var origin = context.Request.Headers[HeaderNames.Origin];
        if (origin.Count == 0 || !GuardedPaths.Contains(context.Request.Path) || _allowed.Contains(origin.ToString()))
            return next(context);

        return Results.Problem("This origin is not allowed.", statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
    }
}
