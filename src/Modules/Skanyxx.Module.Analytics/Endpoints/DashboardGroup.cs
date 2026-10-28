using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary>The Dashboard's endpoints, CORS off. Each endpoint names its roles: the owner, and the supervisor read-only.</summary>
internal sealed class DashboardGroup : Group
{
    public DashboardGroup() => Configure("api/dashboard", ep => ep.Options(b => b.WithMetadata(new DisableCorsAttribute())));
}
