using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;

namespace Skanyxx.Module.Analytics.Endpoints;

/// <summary>Every analytics endpoint: a signed-in person (FastEndpoints' default), CORS off. Scope checks are the handlers'.</summary>
internal sealed class AnalyticsGroup : Group
{
    public AnalyticsGroup() => Configure("api/analytics", ep => ep.Options(b => b.WithMetadata(new DisableCorsAttribute())));
}
