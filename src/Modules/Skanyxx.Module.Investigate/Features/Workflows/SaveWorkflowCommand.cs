using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Domain;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

/// <summary>Creates a workflow (<paramref name="Id"/> null) or replaces one.</summary>
public sealed record SaveWorkflowCommand(
    Viewer Viewer, Guid? Id, string Name, string Description, string? SharedTeam, List<WorkflowStep> Steps, bool AllowChainedWrites = false)
    : IRequest<Outcome<WorkflowDto>>;
