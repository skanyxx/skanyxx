using MediatR;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Pipelines;

internal sealed class SavePipelineHandler(TicketsDbContext db, TimeProvider clock)
    : IRequestHandler<SavePipelineCommand, Outcome<Pipeline>>
{
    public async Task<Outcome<Pipeline>> Handle(SavePipelineCommand command, CancellationToken ct)
    {
        if (!command.IsSupervisor)
            return Outcome<Pipeline>.Forbidden("Only a supervisor may edit pipelines.");

        var now = clock.GetUtcNow().UtcDateTime;
        var pipeline = await db.Pipelines.SingleOrDefaultAsync(p => p.Id == command.Id, ct);
        var created = pipeline is null;
        pipeline ??= db.Pipelines.Add(new Pipeline { Id = command.Id, Name = command.Name, CreatedAt = now }).Entity;
        pipeline.Name = command.Name;
        pipeline.Description = command.Description;
        pipeline.Stages = [.. command.Stages];
        pipeline.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Outcome<Pipeline>.Conflict(null, $"Pipeline '{command.Id}' was created by someone else just now; re-read it.");
        }

        return created ? Outcome<Pipeline>.Created(pipeline) : Outcome<Pipeline>.Ok(pipeline);
    }
}
