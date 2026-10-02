using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class GetPipelineHandler(TicketsDbContext db) : IRequestHandler<GetPipelineQuery, Outcome<Pipeline>>
{
    public async Task<Outcome<Pipeline>> Handle(GetPipelineQuery query, CancellationToken ct) =>
        await db.Pipelines.AsNoTracking().SingleOrDefaultAsync(p => p.Id == query.Id, ct) is { } pipeline
            ? Outcome<Pipeline>.Ok(pipeline)
            : Outcome<Pipeline>.NotFound($"No pipeline '{query.Id}'.");
}
