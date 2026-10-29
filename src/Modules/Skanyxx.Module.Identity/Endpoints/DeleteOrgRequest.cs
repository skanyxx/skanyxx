using FastEndpoints;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>The slug from the route; where its members (team) or teams (department) go, from the query (D351).</summary>
public sealed class DeleteOrgRequest
{
    public string Slug { get; set; } = "";

    [BindFrom("to")]
    public string? To { get; set; }
}
