using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>AX <c>SuspendTask</c> / <c>ResumeTask</c>: Substrate snapshots memory and <c>/workspace</c>, resume restores them.</summary>
public sealed record SetSandboxSuspendedCommand(string? UserId, bool IsSupervisor, string Name, bool Suspend) : IRequest<Outcome<Sandbox>>;
