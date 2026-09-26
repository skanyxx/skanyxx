using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>
/// A person answers the gate. Approve moves on; reject follows the stage's <c>on_fail</c>, exactly as a failed
/// verdict would. The decision and note are recorded on the attempt; its verdict stays what the agent said.
/// Only the run's creator or a supervisor may decide, as with cancel.
/// </summary>
internal sealed class DecideGateHandler(TicketsDbContext db, RunSignal signal, IOptions<TicketsOptions> options, TimeProvider clock)
    : IRequestHandler<DecideGateCommand, Outcome<Run>>
{
    public async Task<Outcome<Run>> Handle(DecideGateCommand command, CancellationToken ct)
    {
        var run = await db.Runs.Include(r => r.StageRuns).SingleOrDefaultAsync(r => r.Id == command.Id, ct);
        if (run is null)
            return Outcome<Run>.NotFound($"No run '{command.Id}'.");
        if (run.CreatedBy != command.UserId && !options.Value.Supervisors.Contains(command.UserId))
            return Outcome<Run>.Forbidden("Only the person who started the run, or a supervisor, may decide its gate.");
        if (run.State != RunState.AwaitingHuman)
            return Outcome<Run>.Conflict(run, $"The run is {run.State}, not waiting for a decision.");

        var now = clock.GetUtcNow().UtcDateTime;
        var gated = run.StageRuns.Where(s => s.StageId == run.CurrentStage.Id).MaxBy(s => s.Attempt)!;
        var approved = command.Decision == Decision.Approve;
        gated.State = approved ? StageState.Passed : StageState.Failed;
        gated.EndedAt = now;
        gated.DecidedBy = command.UserId;
        gated.Note = command.Note;
        gated.Warnings.Add($"{command.UserId} {(approved ? "approved" : "rejected")}{(command.Note is { Length: > 0 } n ? $": {n}" : "")}");

        run.State = RunState.Running;
        if (approved)
            RunTransitions.Pass(run);
        else
            RunTransitions.Fail(run, gated);
        run.UpdatedAt = now;
        run.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Outcome<Run>.Conflict(null, "The run changed while deciding; re-read it.");
        }

        signal.Notify();
        return Outcome<Run>.Ok((await db.LoadAsync(run.Id, withPrompts: false, ct))!);
    }
}
