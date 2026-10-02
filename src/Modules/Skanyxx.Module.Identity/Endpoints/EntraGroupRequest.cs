namespace Skanyxx.Module.Identity.Endpoints;

public sealed class EntraGroupRequest
{
    public string? GroupId { get; set; }
    public string? Label { get; set; }
    public List<string>? Roles { get; set; }
    public List<string>? Teams { get; set; }
}
