using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

/// <summary><see cref="State"/> is what the run did with the stage; <see cref="Verdict"/> is what the stage said.</summary>
public sealed record StageRunDto(
    string StageId, StageKind Kind, string Title, int Attempt, StageState State, Verdict Verdict, string Output,
    bool Degraded, IReadOnlyList<string> Warnings, DateTime StartedAt, DateTime? EndedAt, string? DecidedBy, string? Note,
    IReadOnlyList<AgentTurnDto> Turns);
