using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>
/// Every agents endpoint: a signed-in person with a role, CORS off so no other origin drives them with the person's
/// cookie, and a body the size of the largest form (a 16k prompt and the lists). The roles on each endpoint are the
/// handlers' rules again (D254), so a refused person never reaches kagent.
/// </summary>
internal sealed class AgentsGroup : Group
{
    public const string Prefix = "api/agents";

    public const int MaxBodyBytes = 128 * 1024;

    public AgentsGroup() => Configure(Prefix, ep => Apply(ep));

    /// <summary>Also applied directly by the endpoints at the prefix itself (a group route <c>""</c> comes out as <c>api/agents/</c>, D179).</summary>
    internal static void Apply(EndpointDefinition ep) =>
        ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
}
