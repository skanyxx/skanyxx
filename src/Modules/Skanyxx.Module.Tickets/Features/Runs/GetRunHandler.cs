using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

internal sealed class GetRunHandler(TicketsDbContext db) : IRequestHandler<GetRunQuery, Outcome<Run>>
{
    public async Task<Outcome<Run>> Handle(GetRunQuery query, CancellationToken ct) =>
        await db.LoadAsync(query.Id, query.WithPrompts, ct) is { } run
            ? Outcome<Run>.Ok(run)
            : Outcome<Run>.NotFound($"No run '{query.Id}'.");
}
