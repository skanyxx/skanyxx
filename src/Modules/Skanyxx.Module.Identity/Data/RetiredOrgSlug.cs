namespace Skanyxx.Module.Identity.Data;

/// <summary>
/// The slug of a deleted department or team (D351). Memory keys scopes on slugs, so one is never given to a new
/// department or team: its old cards would come with it.
/// </summary>
public sealed class RetiredOrgSlug
{
    public const string Department = "department";
    public const string Team = "team";

    public string Kind { get; set; } = "";
    public string Slug { get; set; } = "";
    public string RetiredBy { get; set; } = "";
    public DateTimeOffset RetiredUtc { get; set; }
}
