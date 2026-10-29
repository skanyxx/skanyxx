using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Contracts;

public sealed record RunSummaryDto(
    Guid Id, Guid WorkflowId, string WorkflowName, RunState State, string CreatedBy, bool Mine,
    DateTime CreatedAt, DateTime? StartedAt, DateTime? EndedAt, string? Failure);
