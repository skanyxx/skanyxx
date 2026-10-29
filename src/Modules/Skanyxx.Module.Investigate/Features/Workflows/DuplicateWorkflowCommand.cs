using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

public sealed record DuplicateWorkflowCommand(Viewer Viewer, Guid Id) : IRequest<Outcome<WorkflowDto>>;
