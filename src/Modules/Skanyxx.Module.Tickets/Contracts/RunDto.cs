using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

/// <summary>A run with every attempt of every stage, oldest first.</summary>
public sealed record RunDto(
    Guid Id, string TicketKey, Ticket Ticket, string PipelineId, string PipelineName, IReadOnlyList<PipelineStage> Stages,
    RunState State, int Cursor, IReadOnlyDictionary<string, int> Loops, string? Error, string CreatedBy, DateTime CreatedAt,
    DateTime UpdatedAt, IReadOnlyList<StageRunDto> StageRuns)
{
    /// <summary>
    /// The stage an agent is working on right now. Derived, not stored: a stage row is written only when the
    /// stage ends, and a stored "running" would have to be un-stored after every crash and cancel.
    /// </summary>
    public string? RunningStageId => State == RunState.Running ? Stages[Cursor].Id : null;
}
