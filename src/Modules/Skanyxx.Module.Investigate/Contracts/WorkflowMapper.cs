using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Contracts;

internal static class WorkflowMapper
{
    public static WorkflowSummaryDto Summary(Workflow w, Viewer viewer) =>
        new(w.Id, w.Name, w.Description, w.OwnerId, w.OwnerId == viewer.UserId, w.SharedTeam, WorkflowTree.Flatten(w.Steps).Count(),
            WorkflowTree.Agents(w.Steps), w.UpdatedAt, WorkflowAccess.CanEdit(w, viewer));

    public static WorkflowDto Full(Workflow w, Viewer viewer) =>
        new(w.Id, w.Name, w.Description, w.OwnerId, w.OwnerId == viewer.UserId, w.SharedTeam, w.Steps, w.CreatedAt, w.UpdatedAt,
            WorkflowAccess.CanEdit(w, viewer));
}
