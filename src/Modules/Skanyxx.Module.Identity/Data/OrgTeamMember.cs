namespace Skanyxx.Module.Identity.Data;

/// <summary>A person in a team. Department membership is derived: the departments of one's teams.</summary>
public sealed class OrgTeamMember
{
    public string TeamSlug { get; set; } = "";
    public string UserId { get; set; } = "";
    public string AddedBy { get; set; } = "";
    public DateTimeOffset AddedUtc { get; set; }
}
