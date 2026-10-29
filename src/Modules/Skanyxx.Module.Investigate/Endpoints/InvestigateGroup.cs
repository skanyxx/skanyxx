using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Skanyxx.Core.Platform;

namespace Skanyxx.Module.Investigate.Endpoints;

/// <summary>
/// Every Investigate endpoint: CORS off so no other origin drives them with the person's cookie, a body the size of the
/// largest workflow, and the roles that have the panel (owner, supervisor, builder; never an employee), set on each.
/// </summary>
internal sealed class InvestigateGroup : Group
{
    public const int MaxBodyBytes = 1024 * 1024;

    public static readonly string[] Roles = [SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder];

    public InvestigateGroup() =>
        Configure("api/investigate", ep =>
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
