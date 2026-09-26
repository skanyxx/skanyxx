using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>
/// AX's watch covers start-up only: it closes once the task is Running, Failed or Completed. The stream therefore
/// always ends with one fresh read (<c>final</c>/<c>gone</c>), after which the caller polls <c>GET</c>.
/// </summary>
public sealed record WatchTaskQuery(string? UserId, string Name) : IRequest<Outcome<IAsyncEnumerable<SandboxTaskEvent>>>;
