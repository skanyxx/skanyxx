using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Tasks;

/// <summary>One page of the atespace's tasks, in AX's order.</summary>
public sealed record ListTasksQuery(string? UserId, int Limit, int Offset) : IRequest<Outcome<IReadOnlyList<SandboxTask>>>
{
    public const int MaxLimit = 100;
}
