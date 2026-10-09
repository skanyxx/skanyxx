using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Identity.Audit;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Audit only; it never grants or denies. A signed-in person holding none of an owner route's allowed roles (the
/// people and invite admin, API or page) is refused by the role requirement; this logs that refusal at Warning, as the
/// memory secret refusals are (D085), and writes an audit row (D152) — at most one per route and caller a minute (D167). Anonymous callers are a 401 and are not logged
/// here. The route template is logged, not the path: the path is the caller's to write.
/// </summary>
internal sealed class OwnerRouteRefusals(ILogger<OwnerRouteRefusals> logger) : IAuthorizationHandler
{
    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.Resource is not HttpContext http || context.User.Identity?.IsAuthenticated != true
            || !context.Requirements.OfType<RolesAuthorizationRequirement>()
                .Any(r => r.AllowedRoles.Contains(SkanyxxRoles.Owner) && !r.AllowedRoles.Any(context.User.IsInRole)))
            return;

        var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        var actor = Caller.UserId(context.User);
        logger.LogWarning("Owner-only route {Method} {Route} refused for {ActorUserId} from {RemoteIp}",
            http.Request.Method, route, actor, http.Connection.RemoteIpAddress);
        // Sampled per route and caller (D167): a second person refused on the same route gets their own row. Never
        // cancelled by the caller: an abort must not throw out of authorization.
        await http.RequestServices.GetRequiredService<IdentityAudit>().WriteSampledAsync(AuditActions.OwnerRouteRefused, $"{route}|{actor}", actor, null,
            new { method = http.Request.Method, route }, CancellationToken.None);
    }
}
