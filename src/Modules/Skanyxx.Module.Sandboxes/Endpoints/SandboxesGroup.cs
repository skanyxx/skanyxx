using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Skanyxx.Module.Sandboxes.Endpoints;

/// <summary>
/// Shared settings for every sandboxes endpoint: a signed-in user is required (FastEndpoints' default, against the
/// Host's authentication schemes), CORS stays off so no other origin can drive them with the user's cookie, and the
/// body is capped. While the module is off (<see cref="SandboxesOptions.Enabled"/>) it registered no services, and every route is a 404
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
    internal static void Apply(EndpointDefinition ep) =>
        ep.Options(b => b
            .WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))
            .AddEndpointFilter((context, next) => IsEnabled(context.HttpContext) ? next(context) : ValueTask.FromResult<object?>(Results.NotFound())));

    private static bool IsEnabled(HttpContext context) =>
        SandboxesOptions.IsEnabled(context.RequestServices.GetRequiredService<IConfiguration>());
}
