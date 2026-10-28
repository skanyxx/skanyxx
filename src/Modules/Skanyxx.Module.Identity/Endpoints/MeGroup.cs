using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The signed-in person's own things (<c>api/me</c>): no id in any route, so nobody can name another person's.</summary>
internal sealed class MeGroup : Group
{
    public MeGroup() =>
        Configure("api/me", ep =>
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(IdentityGroup.MaxBodyBytes))));
}
