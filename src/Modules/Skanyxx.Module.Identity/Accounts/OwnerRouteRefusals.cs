using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Identity.Accounts;

/// <summary>
/// Audit only; it never grants or denies. A signed-in person holding none of an owner route's allowed roles (the
/// people and invite admin, API or page) is refused by the role requirement; this logs that refusal at Warning, as the
/// memory secret refusals are (D085). Anonymous callers are a 401 and are not logged here. The route template is
/// logged, not the path: the path is the caller's to write.
/// </summary>
internal sealed class OwnerRouteRefusals(ILogger<OwnerRouteRefusals> logger) : IAuthorizationHandler
{
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        if (context.Resource is HttpContext http && context.User.Identity?.IsAuthenticated == true
            && context.Requirements.OfType<RolesAuthorizationRequirement>()
                .Any(r => r.AllowedRoles.Contains(SkanyxxRoles.Owner) && !r.AllowedRoles.Any(context.User.IsInRole)))
            logger.LogWarning("Owner-only route {Method} {Route} refused for {ActorUserId} from {RemoteIp}",
                http.Request.Method, (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, Caller.UserId(context.User),
                http.Connection.RemoteIpAddress);
        return Task.CompletedTask;
    }
}
