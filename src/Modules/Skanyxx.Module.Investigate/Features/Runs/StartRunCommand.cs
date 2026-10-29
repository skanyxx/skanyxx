using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

public sealed record StartRunCommand(Viewer Viewer, Guid WorkflowId, string Input) : IRequest<Outcome<RunDto>>;
