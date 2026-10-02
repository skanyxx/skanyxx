namespace Skanyxx.Module.Identity.Data;

/// <summary>One mapped Entra group: its object id (lowercase GUID) and the roles and team slugs membership gives (D5).</summary>
public sealed class EntraGroupMap
{
    public string GroupId { get; set; } = "";
    public string? Label { get; set; }
    public string[] Roles { get; set; } = [];
    public string[] Teams { get; set; } = [];
}
