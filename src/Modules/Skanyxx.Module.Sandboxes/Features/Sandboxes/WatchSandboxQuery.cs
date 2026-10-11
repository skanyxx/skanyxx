using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>
/// AX's watch covers start-up only: it closes once the sandbox is Running, Failed or Completed. The stream therefore
/// always ends with one fresh read (<c>final</c>/<c>gone</c>), after which the caller polls <c>GET</c>.
/// </summary>
public sealed record WatchSandboxQuery(string? UserId, string Name) : IRequest<Outcome<IAsyncEnumerable<SandboxEvent>>>;
