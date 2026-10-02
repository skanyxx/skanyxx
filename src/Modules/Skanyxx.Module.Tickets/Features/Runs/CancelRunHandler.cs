using MediatR;
using Microsoft.EntityFrameworkCore;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>
/// Cancel wins over an in-flight stage: the worker's save then fails its version check and is dropped (the agent
/// call itself runs to its end). Only the run's creator or a supervisor may cancel.
/// </summary>
internal sealed class CancelRunHandler(TicketsDbContext db, TimeProvider clock)
    : IRequestHandler<CancelRunCommand, Outcome<Run>>
{
    public async Task<Outcome<Run>> Handle(CancelRunCommand command, CancellationToken ct)
    {
        var run = await db.Runs.Include(r => r.StageRuns).SingleOrDefaultAsync(r => r.Id == command.Id, ct);
        if (run is null)
            return Outcome<Run>.NotFound($"No run '{command.Id}'.");
        if (run.CreatedBy != command.UserId && !command.IsSupervisor)
            return Outcome<Run>.Forbidden("Only the person who started the run, or a supervisor, may cancel it.");
        if (run.IsTerminal)
            return Outcome<Run>.Conflict(run, $"The run already ended ({run.State}).");

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var waiting in run.StageRuns.Where(s => s.State == StageState.AwaitingHuman))
        {
            waiting.State = StageState.Skipped;
            waiting.EndedAt = now;
            waiting.Warnings.Add($"{command.UserId} cancelled the run at this gate");
        }

        run.State = RunState.Cancelled;
        run.UpdatedAt = now;
        run.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Outcome<Run>.Conflict(null, "The run changed while cancelling; re-read it and try again.");
        }

        return Outcome<Run>.Ok((await db.LoadAsync(run.Id, withPrompts: false, ct))!);
    }
}
