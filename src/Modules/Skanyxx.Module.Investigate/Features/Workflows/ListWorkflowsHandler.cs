using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;

namespace Skanyxx.Module.Investigate.Features.Workflows;

/// <summary>The owner and supervisors see every workflow; a builder their own and those shared to their teams.</summary>
internal sealed class ListWorkflowsHandler(InvestigateDbContext db, OrgTeams org) : IRequestHandler<ListWorkflowsQuery, Outcome<IReadOnlyList<WorkflowSummaryDto>>>
{
    public async Task<Outcome<IReadOnlyList<WorkflowSummaryDto>>> Handle(ListWorkflowsQuery query, CancellationToken ct)
    {
        var viewer = query.Viewer;
        var workflows = db.Workflows.AsNoTracking();
        if (!viewer.IsSupervisor)
        {
            var teams = (await org.MemberOfAsync(viewer, ct)).ToList();
            workflows = workflows.Where(w => w.OwnerId == viewer.UserId || (w.SharedTeam != null && teams.Contains(w.SharedTeam)));
        }

        var list = await workflows.OrderBy(w => w.Name).ThenBy(w => w.CreatedAt).ToListAsync(ct);
        return Outcome<IReadOnlyList<WorkflowSummaryDto>>.Ok([.. list.Select(w => WorkflowMapper.Summary(w, viewer))]);
    }
}
