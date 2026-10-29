using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Data;

namespace Skanyxx.Module.Investigate.Features.Runs;

internal sealed class ListRunsHandler(InvestigateDbContext db) : IRequestHandler<ListRunsQuery, Outcome<IReadOnlyList<RunSummaryDto>>>
{
    public async Task<Outcome<IReadOnlyList<RunSummaryDto>>> Handle(ListRunsQuery query, CancellationToken ct)
    {
        var viewer = query.Viewer;
        var runs = db.Runs.AsNoTracking();
        if (!viewer.IsSupervisor || query.MineOnly)
            runs = runs.Where(r => r.CreatedBy == viewer.UserId);
        if (query.WorkflowId is { } workflow)
            runs = runs.Where(r => r.WorkflowId == workflow);
        if (query.State is { } state)
            runs = runs.Where(r => r.State == state);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            runs = runs.Where(r => EF.Functions.ILike(r.WorkflowName, pattern));
        }

        var list = await runs.OrderByDescending(r => r.CreatedAt).Take(query.Limit).ToListAsync(ct);
        return Outcome<IReadOnlyList<RunSummaryDto>>.Ok([.. list.Select(r => RunMapper.Summary(r, viewer))]);
    }
}
