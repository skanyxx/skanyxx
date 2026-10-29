using FastEndpoints;
using Skanyxx.Core.Platform;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Sandboxes.Endpoints;

/// <summary>
/// Shared settings for every sandboxes endpoint: supervisors and the owner only (D392: a sandbox runs any allowed image
/// on cluster compute, so it is never an employee's or a builder's; the page was supervisor-only while the API was not),
/// CORS off so no other origin can drive them with the user's cookie, and a capped body. While the module is off (<see cref="SandboxesOptions.Enabled"/>) it registered no services, and every route is a 404
/// before any binding, validation or handler runs (after authentication: an anonymous call is a 401 either way).
/// </summary>
internal sealed class SandboxesGroup : Group
{
    /// <summary>64 args × 8k plus 64 env × 8k, JSON-escaped, fits well inside this.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    public const string Prefix = "api/sandboxes";

    public SandboxesGroup() => Configure(Prefix, Apply);

    /// <summary>
    /// Also applied directly by the list endpoint, which sits at the prefix itself: a group route <c>""</c> would come out
    /// as <c>api/sandboxes/</c> (D179).
    /// </summary>
    internal static void Apply(EndpointDefinition ep)
    {
        ep.Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
        ep.Options(b => b
            .WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))
            .AddEndpointFilter((context, next) => IsEnabled(context.HttpContext) ? next(context) : ValueTask.FromResult<object?>(Results.NotFound())));
    }

    private static bool IsEnabled(HttpContext context) =>
        SandboxesOptions.IsEnabled(context.RequestServices.GetRequiredService<IConfiguration>());
}
