using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
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
/// Allowed: <see cref="SkanyxxOptions.AllowedOrigins"/>, plus the origin of <c>Identity:PublicBaseUrl</c> (where people
/// reach Skanyxx, so its pages' own origin), read as configuration so Core needs nothing from the Identity module.
/// Non-browser clients (kagent, curl, MCP SDKs) send no <c>Origin</c> and pass. The one exception is the Microsoft
/// sign-in callback (<see cref="GuardedPaths.ExternalSignInCallback"/>), which the OIDC handler protects itself.
/// </summary>
public sealed class OriginGuardMiddleware
{
    public const string PublicBaseUrlKey = "Identity:PublicBaseUrl";

    private readonly RequestDelegate _next;
    private readonly HashSet<string> _allowed;

    public OriginGuardMiddleware(RequestDelegate next, IOptions<SkanyxxOptions> options, IConfiguration configuration,
        IHostEnvironment environment, ILogger<OriginGuardMiddleware> logger)
    {
        _next = next;
        _allowed = new(options.Value.AllowedOrigins.Select(o => o.TrimEnd('/')), StringComparer.OrdinalIgnoreCase);
        if (Uri.TryCreate(configuration[PublicBaseUrlKey], UriKind.Absolute, out var publicBase) && publicBase.Scheme is "http" or "https")
            _allowed.Add(publicBase.GetLeftPart(UriPartial.Authority));
        if (_allowed.Count == 0 && !environment.IsDevelopment())
            logger.LogWarning(
                "Neither Skanyxx:AllowedOrigins nor Identity:PublicBaseUrl is set: browser calls to /api/chat, /api/model and the " +
                "other guarded APIs are refused (403), so Chat cannot send. Set Identity:PublicBaseUrl to the address people use.");
    }

    public Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var origin = request.Headers[HeaderNames.Origin].ToString();
        if (origin.Length == 0 || _allowed.Contains(origin) || GuardedPaths.IsExternalSignInCallback(request.Path))
            return _next(context);
        if (!GuardedPaths.Contains(request.Path) && (IsSafe(request.Method) || IsSameOrigin(origin, request)))
            return _next(context);

        return Results.Problem("This origin is not allowed.", statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    // Host and port only: behind a TLS-terminating proxy the request scheme is http while the page's is https.
    private static bool IsSameOrigin(string origin, HttpRequest request) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
}
