using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>AX <c>DeleteTask</c>: asynchronous (the task goes Terminating, then disappears). Returns the task as it was.</summary>
public sealed record StopTaskCommand(string? UserId, bool IsSupervisor, string Name) : IRequest<Outcome<SandboxTask>>;
