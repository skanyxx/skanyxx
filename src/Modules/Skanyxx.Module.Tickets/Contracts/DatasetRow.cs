using Skanyxx.Module.Tickets.Domain;

namespace Skanyxx.Module.Tickets.Contracts;

/// <summary>One agent turn: the unit of evidence. Unique by (run, stage, attempt, agent).</summary>
public sealed record DatasetRow(
    Guid RunId, string Ticket, string PipelineId, string StageId, StageKind Kind, int Attempt, string AgentId, string Agent,
    string Prompt, string Output, StageState State, Verdict Verdict, bool Degraded, string? DecidedBy,
    DateTime StartedAt, DateTime EndedAt);
