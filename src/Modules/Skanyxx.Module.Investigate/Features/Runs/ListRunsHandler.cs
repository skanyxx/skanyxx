using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>
/// The owner sees every run, a supervisor theirs and those of the people in their teams (D371), everyone else their own.
/// Projected to the summary in the query: a list never loads a run's step tree, input or answers.
/// </summary>
internal sealed class ListRunsHandler(InvestigateDbContext db, OrgTeams org) : IRequestHandler<ListRunsQuery, Outcome<IReadOnlyList<RunSummaryDto>>>
{
    public async Task<Outcome<IReadOnlyList<RunSummaryDto>>> Handle(ListRunsQuery query, CancellationToken ct)
    {
        var viewer = query.Viewer;
        var me = viewer.UserId;
        var runs = db.Runs.AsNoTracking();
        if (query.MineOnly)
            runs = runs.Where(r => r.CreatedBy == me);
        else if (!viewer.IsOwner)
        {
            var people = viewer.IsSupervisor ? (await org.ReachAsync(viewer, ct)).People.ToList() : [];
            runs = runs.Where(r => r.CreatedBy == me || people.Contains(r.CreatedBy));
        }
        if (query.WorkflowId is { } workflow)
            runs = runs.Where(r => r.WorkflowId == workflow);
        if (query.State is { } state)
            runs = runs.Where(r => r.State == state);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            runs = runs.Where(r => EF.Functions.ILike(r.WorkflowName, pattern));
        }

        var list = await runs.OrderByDescending(r => r.CreatedAt).Take(query.Limit)
            .Select(r => new RunSummaryDto(r.Id, r.WorkflowId, r.WorkflowName, r.State, r.CreatedBy, r.CreatedBy == me, r.CreatedAt, r.StartedAt, r.EndedAt, r.Failure))
            .ToListAsync(ct);
        return Outcome<IReadOnlyList<RunSummaryDto>>.Ok(list);
    }
}
