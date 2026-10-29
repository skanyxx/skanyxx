using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// The one place deciding who may do what (D341). A workflow someone may not see is "not found" to them, never
/// "forbidden": its name and existence are not theirs to learn.
/// </summary>
public static class WorkflowAccess
{
    public static bool CanSee(Workflow workflow, Viewer viewer, IReadOnlySet<string> teams) =>
        viewer.IsSupervisor || workflow.OwnerId == viewer.UserId || (workflow.SharedTeam is { } team && teams.Contains(team));

    /// <summary>Edit and delete: the workflow's owner, or the owner/a supervisor. Team members only run and duplicate.</summary>
    public static bool CanEdit(Workflow workflow, Viewer viewer) => viewer.IsSupervisor || workflow.OwnerId == viewer.UserId;

    /// <summary>A run is its starter's; the owner and supervisors see every run.</summary>
    public static bool CanSee(InvestigationRun run, Viewer viewer) => viewer.IsSupervisor || run.CreatedBy == viewer.UserId;
}
