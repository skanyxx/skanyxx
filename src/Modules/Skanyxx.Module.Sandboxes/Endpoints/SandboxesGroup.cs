using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Sandboxes.Endpoints;

/// <summary>
/// Shared settings for every sandboxes endpoint. CORS is off for the same reason as memory and tickets: identity is
/// a plain header until the identity slice, so no web page may drive these from an employee's browser.
/// While the module is off (<see cref="SandboxesOptions.Enabled"/>) it registered no services, and every route is a 404
/// before any binding, validation or handler runs.
/// </summary>
internal sealed class SandboxesGroup : Group
{
    /// <summary>64 args × 8k plus 64 env × 8k, JSON-escaped, fits well inside this.</summary>
    public const int MaxBodyBytes = 2 * 1024 * 1024;

    public SandboxesGroup() =>
        Configure("api/sandboxes", ep =>
        {
            ep.AllowAnonymous(); // TODO(identity-slice)
            ep.Options(b => b
                .WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))
                .AddEndpointFilter((context, next) => IsEnabled(context.HttpContext) ? next(context) : ValueTask.FromResult<object?>(Results.NotFound())));
        });

    private static bool IsEnabled(HttpContext context) =>
        SandboxesOptions.IsEnabled(context.RequestServices.GetRequiredService<IConfiguration>());
}
