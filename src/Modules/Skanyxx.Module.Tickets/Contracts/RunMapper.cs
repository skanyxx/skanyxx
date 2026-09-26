using Riok.Mapperly.Abstractions;
using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

[Mapper]
public static partial class RunMapper
{
    [MapperIgnoreSource(nameof(Run.LoopedFrom))]
    [MapperIgnoreSource(nameof(Run.Version))]
    [MapperIgnoreSource(nameof(Run.CurrentStage))]
    [MapperIgnoreSource(nameof(Run.IsTerminal))]
    private static partial RunDto Map(Run run);

    [MapperIgnoreSource(nameof(StageRun.Id))]
    [MapperIgnoreSource(nameof(StageRun.RunId))]
    private static partial StageRunDto Map(StageRun stage);

    [MapperIgnoreSource(nameof(AgentTurn.Id))]
    [MapperIgnoreSource(nameof(AgentTurn.StageRunId))]
    [MapperIgnoreSource(nameof(AgentTurn.Prompt))]
    [MapperIgnoreSource(nameof(AgentTurn.Output))]
    private static partial AgentTurnDto Map(AgentTurn turn);


    /// <summary>Attempts in the order they happened (id is insertion order).</summary>
    public static RunDto ToDto(this Run run) =>
        Map(run) with { StageRuns = run.StageRuns.OrderBy(s => s.Id).Select(Map).ToList() };

    public static IEnumerable<DatasetRow> ToDataset(this Run run) =>
        from stage in run.StageRuns.OrderBy(s => s.Id)
        from turn in stage.Turns.OrderBy(t => t.Id)
        select new DatasetRow(run.Id, run.TicketKey, run.PipelineId, stage.StageId, stage.Kind, stage.Attempt, turn.AgentId,
            turn.Agent, turn.Prompt, turn.Output, stage.State, stage.Verdict, turn.Degraded, stage.DecidedBy, turn.StartedAt, turn.EndedAt);
}
