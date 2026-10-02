using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class ListPipelinesHandler(TicketsDbContext db) : IRequestHandler<ListPipelinesQuery, Outcome<IReadOnlyList<Pipeline>>>
{
    public async Task<Outcome<IReadOnlyList<Pipeline>>> Handle(ListPipelinesQuery query, CancellationToken ct) =>
        Outcome<IReadOnlyList<Pipeline>>.Ok(await db.Pipelines.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct));
}
