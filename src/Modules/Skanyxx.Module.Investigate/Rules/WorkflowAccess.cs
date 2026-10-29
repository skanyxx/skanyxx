using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Rules;

/// <summary>
/// The one place deciding who may do what (D341, D371). The owner reaches everything; a supervisor the work of the people
/// in their own teams; everyone their own, and the workflows shared to their teams. Something one may not reach is "not
/// found", never "forbidden": its name and existence are not theirs to learn.
/// </summary>
public static class WorkflowAccess
{
    public static bool CanSee(Workflow workflow, Viewer viewer, Reach reach) =>
        CanEdit(workflow, viewer, reach) || (workflow.SharedTeam is { } team && reach.Teams.Contains(team));

    /// <summary>Edit and delete: its owner, the owner, or a supervisor of its owner's team. Team members only run and duplicate.</summary>
    public static bool CanEdit(Workflow workflow, Viewer viewer, Reach reach) =>
        viewer.IsOwner || workflow.OwnerId == viewer.UserId || Supervises(viewer, reach, workflow.OwnerId);

    /// <summary>A run, its input and its answers: its starter, the owner, and a supervisor of the starter's team.</summary>
    public static bool CanSee(InvestigationRun run, Viewer viewer, Reach reach) =>
        viewer.IsOwner || run.CreatedBy == viewer.UserId || Supervises(viewer, reach, run.CreatedBy);

    private static bool Supervises(Viewer viewer, Reach reach, string userId) => viewer.IsSupervisor && reach.People.Contains(userId);
}
