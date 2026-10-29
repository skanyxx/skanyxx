using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Audit;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

/// <summary>
/// Deletes a workflow: its owner, the owner, or a supervisor of its owner's team; nobody else. Its runs stay in the history (they hold their
/// own copy of the steps), so a report is never lost with the workflow.
/// </summary>
internal sealed class DeleteWorkflowHandler(InvestigateDbContext db, OrgTeams org, InvestigateAudit audit) : IRequestHandler<DeleteWorkflowCommand, Outcome<Guid>>
{
    public async Task<Outcome<Guid>> Handle(DeleteWorkflowCommand command, CancellationToken ct)
    {
        var reach = await org.ReachAsync(command.Viewer, ct);
        var workflow = await db.Workflows.SingleOrDefaultAsync(w => w.Id == command.Id, ct);
        if (workflow is null || !WorkflowAccess.CanSee(workflow, command.Viewer, reach))
            return Outcome<Guid>.NotFound("No such workflow.");
        if (!WorkflowAccess.CanEdit(workflow, command.Viewer, reach))
            return Outcome<Guid>.Forbidden("Only its owner or a supervisor can delete this workflow.");

        db.Workflows.Remove(workflow);
        await db.SaveChangesAsync(ct);
        await audit.WorkflowAsync(InvestigateAudit.WorkflowDeleted, command.Viewer.UserId, workflow.Id, workflow.Name, WorkflowTree.Flatten(workflow.Steps).Count(), workflow.SharedTeam);
        return Outcome<Guid>.Ok(workflow.Id);
    }
}
