using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class DeletePipelineHandler(TicketsDbContext db)
    : IRequestHandler<DeletePipelineCommand, Outcome<Pipeline>>
{
    public async Task<Outcome<Pipeline>> Handle(DeletePipelineCommand command, CancellationToken ct)
    {
        if (!command.IsSupervisor)
            return Outcome<Pipeline>.Forbidden("Only a supervisor may delete pipelines.");

        var pipeline = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == command.Id, ct);
        if (pipeline is null)
            return Outcome<Pipeline>.NotFound($"No pipeline '{command.Id}'.");
        db.Pipelines.Remove(pipeline);
        await db.SaveChangesAsync(ct);
        return Outcome<Pipeline>.Ok(pipeline);
    }
}
