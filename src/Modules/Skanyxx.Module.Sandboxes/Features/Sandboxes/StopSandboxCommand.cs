using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>AX <c>DeleteTask</c>: asynchronous (the sandbox goes Terminating, then disappears). Returns the sandbox as it was.</summary>
public sealed record StopSandboxCommand(string? UserId, bool IsSupervisor, string Name) : IRequest<Outcome<Sandbox>>;
