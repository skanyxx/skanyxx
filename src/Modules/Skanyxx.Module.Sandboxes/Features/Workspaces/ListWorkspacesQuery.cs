using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

public sealed record ListWorkspacesQuery(string? UserId) : IRequest<Outcome<IReadOnlyList<SandboxWorkspace>>>;
