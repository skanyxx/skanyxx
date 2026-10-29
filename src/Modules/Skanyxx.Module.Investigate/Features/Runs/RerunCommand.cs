using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Investigate.Contracts;
using Skanyxx.Module.Investigate.Rules;

namespace Skanyxx.Module.Investigate.Features.Runs;

/// <summary>The same input again, through the workflow as it is now (it must still exist and be visible).</summary>
public sealed record RerunCommand(Viewer Viewer, Guid Id) : IRequest<Outcome<RunDto>>;
