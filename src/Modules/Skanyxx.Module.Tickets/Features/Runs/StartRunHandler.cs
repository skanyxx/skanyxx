using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Tickets.Data;
using Skanyxx.Module.Tickets.Domain;
using Skanyxx.Module.Tickets.Engine;
using Skanyxx.Module.Tickets.Sources;

namespace Skanyxx.Module.Tickets.Features.Runs;

/// <summary>Snapshots the ticket and the pipeline, queues the run and wakes the worker. The work itself is async.</summary>
internal sealed class StartRunHandler(
    TicketsDbContext db, ITicketSource source, RunSignal signal, IOptions<TicketsOptions> options, TimeProvider clock)
    : IRequestHandler<StartRunCommand, Outcome<Run>>
{
    private const int RunLockKey = 0x544B5401;

    public async Task<Outcome<Run>> Handle(StartRunCommand command, CancellationToken ct)
    {
        var pipeline = await db.Pipelines.AsNoTracking().SingleOrDefaultAsync(p => p.Id == command.PipelineId, ct);
        if (pipeline is null)
            return Outcome<Run>.NotFound($"No pipeline '{command.PipelineId}'.");

        Ticket ticket;
        try
        {
            ticket = await source.GetAsync(command.TicketKey, ct);
        }
        catch (TicketNotFoundException ex)
        {
            return Outcome<Run>.NotFound(ex.Message);
        }
        catch (TicketSourceException ex)
        {
            return Outcome<Run>.Unavailable(ex.Message);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var run = new Run
        {
            Id = Guid.NewGuid(), TicketKey = ticket.Key, Ticket = ticket, PipelineId = pipeline.Id, PipelineName = pipeline.Name,
            Stages = pipeline.Stages, State = RunState.Pending, CreatedBy = command.UserId!, CreatedAt = now, UpdatedAt = now
        };
        // Each run costs model turns; neither one caller nor everyone together can queue without bound. Counted and
        // inserted under one global lock, so parallel requests cannot all pass the counts (starts are rare; a Jira
        // read precedes each). Runs parked at a gate count too: approving one resumes it.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({RunLockKey}, 0)", ct);
            var unfinished = db.Runs.Where(r => r.State == RunState.Pending || r.State == RunState.Running || r.State == RunState.AwaitingHuman);
            var mine = await unfinished.CountAsync(r => r.CreatedBy == command.UserId, ct);
            if (mine >= options.Value.MaxActiveRunsPerUser)
                return Outcome<Run>.RateLimited($"You already have {mine} unfinished runs (Tickets:MaxActiveRunsPerUser); finish or cancel one first.");
            if (await unfinished.CountAsync(ct) >= options.Value.MaxActiveRuns)
                return Outcome<Run>.RateLimited("Too many unfinished runs overall (Tickets:MaxActiveRuns); try again later.");
            db.Runs.Add(run);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        signal.Notify();
        return Outcome<Run>.Accepted(run);
    }
}
