using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// Writes one step's state, each through its own short-lived context so parallel steps never share one. On
/// <see cref="CancellationToken.None"/>: a cancelled or failed step must still be recorded as such.
/// </summary>
internal sealed class StepWriter(IDbContextFactory<InvestigateDbContext> dbs, TimeProvider clock)
{
    public async Task StartedAsync(Guid runId, string stepId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
        await db.StepRuns.Where(s => s.RunId == runId && s.StepId == stepId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Running).SetProperty(x => x.StartedAt, now), CancellationToken.None);
    }

    public async Task EndedAsync(Guid runId, string stepId, StepState state, string? output = null, string? error = null)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
        await db.StepRuns.Where(s => s.RunId == runId && s.StepId == stepId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.EndedAt, now)
                .SetProperty(x => x.Output, output)
                .SetProperty(x => x.Error, error), CancellationToken.None);
    }
}
