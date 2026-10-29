namespace Skanyxx.Module.Investigate.Contracts;

/// <summary>A workflow in the list. <paramref name="Mine"/>: the viewer owns it.</summary>
public sealed record WorkflowSummaryDto(
    Guid Id, string Name, string Description, string OwnerId, bool Mine, string? SharedTeam, int StepCount,
    IReadOnlyList<string> Agents, DateTime UpdatedAt, bool CanEdit);
