namespace Skanyxx.Module.Identity.Data;

/// <summary>A team, always inside one department. The slug never changes and is never reused: memory keys on it.</summary>
public sealed class OrgTeam
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string DepartmentSlug { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}
