using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Contracts;

/// <summary>A run with its step tree (as it was when it started) and each step's progress. <paramref name="Card"/>: <c>scope/key</c>.</summary>
public sealed record RunDto(
    Guid Id, Guid WorkflowId, string WorkflowName, RunState State, string CreatedBy, bool Mine, string Input,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? EndedAt, string? Failure, bool CancelRequested,
    string? CardScope, string? CardKey, IReadOnlyList<WorkflowStep> Steps, IReadOnlyList<StepRunDto> StepRuns);
