using FastEndpoints;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Bound from the route's <c>{slug}</c> and <c>{userId}</c> only.</summary>
public sealed class TeamMemberRequest
{
    [DontBind(Source.QueryParam)]
    public string Slug { get; set; } = "";

    [DontBind(Source.QueryParam)]
    public string UserId { get; set; } = "";
}
