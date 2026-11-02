using Microsoft.EntityFrameworkCore;
using Skanyxx.Module.Investigate.Data;
using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Engine;

/// <summary>
/// Writes one step's state, each through its own short-lived context so parallel steps never share one. On
/// <see cref="CancellationToken.None"/>: a cancelled or failed step must still be recorded as such. Times come from the
/// database's clock, as the run's own do, so replicas whose clocks disagree never start a step before its run (D373).
/// </summary>
internal sealed class StepWriter(IDbContextFactory<InvestigateDbContext> dbs)
{
    public async Task StartedAsync(Guid runId, string stepId)
    {
        await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
        await db.StepRuns.Where(s => s.RunId == runId && s.StepId == stepId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, StepState.Running).SetProperty(x => x.StartedAt, x => DateTime.UtcNow), CancellationToken.None);
    }

    public async Task EndedAsync(Guid runId, string stepId, StepState state, string? output = null, string? error = null, string? kagentReason = null)
    {
        await using var db = await dbs.CreateDbContextAsync(CancellationToken.None);
        await db.StepRuns.Where(s => s.RunId == runId && s.StepId == stepId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.State, state)
                .SetProperty(x => x.EndedAt, x => DateTime.UtcNow)
                .SetProperty(x => x.Output, output)
                .SetProperty(x => x.Error, error)
                .SetProperty(x => x.KagentReason, kagentReason), CancellationToken.None);
    }
}
