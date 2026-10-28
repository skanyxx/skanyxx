using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.ToolServers.Endpoints;

/// <summary>
/// Every tool servers endpoint: CORS off so no other origin drives them with the person's cookie, and a body the size of
/// the largest form (ten headers of 8 KB). Roles are on each endpoint: the owner writes, a supervisor reads and refreshes.
/// </summary>
internal sealed class ToolServersGroup : Group
{
    public const string Prefix = "api/toolservers";

    public const int MaxBodyBytes = 128 * 1024;

    public ToolServersGroup() => Configure(Prefix, ep => Apply(ep));

    /// <summary>Also applied directly by the endpoints at the prefix itself (a group route <c>""</c> comes out as <c>api/toolservers/</c>, D179).</summary>
    internal static void Apply(EndpointDefinition ep) =>
        ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
}
