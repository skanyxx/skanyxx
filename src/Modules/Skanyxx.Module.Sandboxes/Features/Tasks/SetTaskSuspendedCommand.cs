using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>AX <c>SuspendTask</c> / <c>ResumeTask</c>: Substrate snapshots memory and <c>/workspace</c>, resume restores them.</summary>
public sealed record SetTaskSuspendedCommand(string? UserId, bool IsSupervisor, string Name, bool Suspend) : IRequest<Outcome<SandboxTask>>;
