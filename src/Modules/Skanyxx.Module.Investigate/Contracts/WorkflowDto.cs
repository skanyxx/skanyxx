using Skanyxx.Module.Investigate.Domain;

namespace Skanyxx.Module.Investigate.Contracts;

public sealed record WorkflowDto(
    Guid Id, string Name, string Description, string OwnerId, bool Mine, string? SharedTeam, IReadOnlyList<WorkflowStep> Steps,
    DateTime CreatedAt, DateTime UpdatedAt, bool CanEdit, bool AllowChainedWrites);
