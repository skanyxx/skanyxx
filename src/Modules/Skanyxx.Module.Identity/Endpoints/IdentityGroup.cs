using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>CORS off (credentials are never readable cross-origin) and small bodies. Anonymous endpoints opt out one by one.</summary>
internal sealed class IdentityGroup : Group
{
    public const int MaxBodyBytes = 16 * 1024;

    public IdentityGroup() =>
        Configure("api/identity", ep =>
            ep.Options(b => b.WithMetadata(new DisableCorsAttribute(), new RequestSizeLimitAttribute(MaxBodyBytes))));
}
