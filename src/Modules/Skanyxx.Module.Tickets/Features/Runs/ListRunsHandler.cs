using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Contracts;
using Skanyxx.Module.Tickets.Data;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>Projected in the query: the ticket and stage snapshots (jsonb) are not read for a list.</summary>
internal sealed class ListRunsHandler(TicketsDbContext db) : IRequestHandler<ListRunsQuery, Outcome<IReadOnlyList<RunSummaryDto>>>
{
    public async Task<Outcome<IReadOnlyList<RunSummaryDto>>> Handle(ListRunsQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<RunSummaryDto>>.Ok(await db.Runs.AsNoTracking()
            .Where(r => query.TicketKey == null || r.TicketKey == query.TicketKey)
            .OrderByDescending(r => r.CreatedAt)
            .Take(ListRunsQuery.Limit)
            .Select(r => new RunSummaryDto(r.Id, r.TicketKey, r.PipelineId, r.PipelineName, r.State, r.Cursor, r.Error,
                r.CreatedBy, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct));
}
