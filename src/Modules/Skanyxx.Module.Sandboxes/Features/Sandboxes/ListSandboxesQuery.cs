using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Sandboxes;

/// <summary>One page of the atespace's sandboxes, in AX's order.</summary>
public sealed record ListSandboxesQuery(string? UserId, int Limit, int Offset) : IRequest<Outcome<IReadOnlyList<Sandbox>>>
{
    public const int MaxLimit = 100;
}
