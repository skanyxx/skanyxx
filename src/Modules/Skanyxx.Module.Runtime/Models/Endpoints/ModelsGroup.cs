using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Runtime.Models.Endpoints;

/// <summary>
/// Every models endpoint: CORS off so no other origin drives them with the person's cookie, and a small body. Roles per
/// endpoint: the owner writes, a supervisor reads, nobody else (roles.md).
/// </summary>
internal sealed class ModelsGroup : Group
{
    public const string Prefix = "api/models";

    public const int MaxBodyBytes = 16 * 1024;

    public ModelsGroup() => Configure(Prefix, ep => Apply(ep));

    /// <summary>Also applied directly by the endpoints at the prefix itself (a group route <c>""</c> comes out as <c>api/models/</c>, D179).</summary>
    internal static void Apply(EndpointDefinition ep) =>
        ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes)));
}
