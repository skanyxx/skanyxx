using FastEndpoints;

namespace Skanyxx.Module.Identity.Endpoints;

/// <summary>
/// Create: slug and name from the body. Rename: the slug from the route (a body slug is overridden: slugs never
/// change). Never from the query string. Nullable because JSON can send null; the validator turns that into a 400.
/// </summary>
public sealed class DepartmentRequest
{
    [DontBind(Source.QueryParam)]
    public string? Slug { get; set; }

    [DontBind(Source.QueryParam)]
    public string? Name { get; set; }
}
