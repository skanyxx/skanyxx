namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>Bound from the route's <c>{id}</c>.</summary>
public sealed class IdRequest
{
    public string Id { get; set; } = "";
}
