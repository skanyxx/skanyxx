using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;

namespace Skanyxx.Module.Investigate.Features.Workflows;

/// <summary>
/// The owner sees every workflow; everyone else their own and those shared to their teams, and a supervisor also those
/// of the people in their teams (D371).
/// </summary>
internal sealed class ListWorkflowsHandler(InvestigateDbContext db, OrgTeams org) : IRequestHandler<ListWorkflowsQuery, Outcome<IReadOnlyList<WorkflowSummaryDto>>>
{
    public async Task<Outcome<IReadOnlyList<WorkflowSummaryDto>>> Handle(ListWorkflowsQuery query, CancellationToken ct)
    {
        var viewer = query.Viewer;
        var reach = await org.ReachAsync(viewer, ct);
        var workflows = db.Workflows.AsNoTracking();
        if (!viewer.IsOwner)
        {
            var teams = reach.Teams.ToList();
            var people = viewer.IsSupervisor ? reach.People.ToList() : [];
            workflows = workflows.Where(w => w.OwnerId == viewer.UserId || (w.SharedTeam != null && teams.Contains(w.SharedTeam)) || people.Contains(w.OwnerId));
        }

        var list = await workflows.OrderBy(w => w.Name).ThenBy(w => w.CreatedAt).ToListAsync(ct);
        return Outcome<IReadOnlyList<WorkflowSummaryDto>>.Ok([.. list.Select(w => WorkflowMapper.Summary(w, viewer, reach))]);
    }
}
