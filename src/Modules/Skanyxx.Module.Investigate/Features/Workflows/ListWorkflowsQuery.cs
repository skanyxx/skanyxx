using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Workflows;

public sealed record ListWorkflowsQuery(Viewer Viewer) : IRequest<Outcome<IReadOnlyList<WorkflowSummaryDto>>>;
