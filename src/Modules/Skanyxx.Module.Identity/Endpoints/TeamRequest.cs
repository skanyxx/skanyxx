using FastEndpoints;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>As <see cref="DepartmentRequest"/>, plus the department the team is in (or moves to).</summary>
public sealed class TeamRequest
{
    [DontBind(Source.QueryParam)]
    public string? Slug { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Name { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Department { get; set; }
}
