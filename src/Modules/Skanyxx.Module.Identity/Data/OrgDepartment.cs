namespace Skanyxx.Module.Identity.Data;

/// <summary>A department of the org tree (D055). The slug never changes and is never reused: memory keys on it.</summary>
public sealed class OrgDepartment
{
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; }
}
