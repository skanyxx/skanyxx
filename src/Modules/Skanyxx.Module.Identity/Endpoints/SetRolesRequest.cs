namespace Skanyxx.Module.Identity.Endpoints;

public sealed class SetRolesRequest
{
    /// <summary>Bound from the route's <c>{id}</c>.</summary>
    public string Id { get; set; } = "";

    public List<string>? Roles { get; set; }
}
