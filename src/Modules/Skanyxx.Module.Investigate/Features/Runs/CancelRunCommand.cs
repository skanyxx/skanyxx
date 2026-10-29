using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

public sealed record CancelRunCommand(Viewer Viewer, Guid Id) : IRequest<Outcome<RunDto>>;
