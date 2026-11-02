using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Contracts;

internal static class RunMapper
{
    public static RunDto Full(InvestigationRun r, Viewer viewer) =>
        new(r.Id, r.WorkflowId, r.WorkflowName, r.State, r.CreatedBy, r.CreatedBy == viewer.UserId, r.Input, r.CreatedAt, r.StartedAt,
            r.EndedAt, r.Failure, r.CancelRequested, r.CardScope, r.CardKey, r.Steps,
            [.. r.StepRuns.Select(s => new StepRunDto(s.StepId, s.State, s.StartedAt, s.EndedAt, s.Output, s.Error,
                viewer.SeesAgentInternals ? s.KagentReason : null))]);
}
