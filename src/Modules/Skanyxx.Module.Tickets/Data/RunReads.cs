using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Data;

/// <summary>
/// Reads a run with its attempts and agent turns. Prompts and answers of turns (up to ~100k each) are loaded only
/// when asked for — the dataset needs them, a polled run view does not.
/// </summary>
internal static class RunReads
{
    public static async Task<Run?> LoadAsync(this TicketsDbContext db, Guid id, bool withPrompts, CancellationToken ct)
    {
        var run = await db.Runs.AsNoTracking().Include(r => r.StageRuns).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
            return null;

        var stageRunIds = run.StageRuns.Select(s => s.Id).ToList();
        var turns = db.AgentTurns.AsNoTracking().Where(t => stageRunIds.Contains(t.StageRunId));
        var loaded = withPrompts
            ? await turns.ToListAsync(ct)
            : await turns.Select(t => new AgentTurn
            {
                Id = t.Id, StageRunId = t.StageRunId, AgentId = t.AgentId, Agent = t.Agent, Prompt = "", Output = "",
                Degraded = t.Degraded, StartedAt = t.StartedAt, EndedAt = t.EndedAt
            }).ToListAsync(ct);
        var byStage = loaded.ToLookup(t => t.StageRunId);
        foreach (var stage in run.StageRuns)
            stage.Turns = [.. byStage[stage.Id]];
        return run;
    }
}
