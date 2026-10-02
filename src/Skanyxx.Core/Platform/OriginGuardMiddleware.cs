using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Skanyxx.Core.Platform;

/// <summary>
/// Refuses browser origins that are not explicitly allowed:
/// <list type="bullet">
/// <item>on the guarded routes, for every method: <c>DisableCors</c> only stops cross-origin reads, and a DNS-rebound
/// page is same-origin;</item>
/// <item>on every other route, for unsafe methods: SameSite=Lax still sends the session cookie from a same-site
/// origin (another port on the host, a sibling subdomain), so a form there could drive a legacy POST. The page's own
/// origin passes, so the app's forms and fetches keep working; the host name itself is already held to
/// <c>AllowedHosts</c>.</item>
/// </list>
/// Non-browser clients (kagent, curl, MCP SDKs) send no <c>Origin</c> and pass. The one exception is the Microsoft
/// sign-in callback (<see cref="GuardedPaths.ExternalSignInCallback"/>), which the OIDC handler protects itself.
/// </summary>
public sealed class OriginGuardMiddleware(RequestDelegate next, IOptions<SkanyxxOptions> options)
{
    private readonly HashSet<string> _allowed = new(
        options.Value.AllowedOrigins.Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var origin = request.Headers[HeaderNames.Origin].ToString();
        if (origin.Length == 0 || _allowed.Contains(origin) || GuardedPaths.IsExternalSignInCallback(request.Path))
            return next(context);
        if (!GuardedPaths.Contains(request.Path) && (IsSafe(request.Method) || IsSameOrigin(origin, request)))
            return next(context);

        return Results.Problem("This origin is not allowed.", statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    // Host and port only: behind a TLS-terminating proxy the request scheme is http while the page's is https.
    private static bool IsSameOrigin(string origin, HttpRequest request) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
}
