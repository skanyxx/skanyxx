using MediatR;
using Microsoft.Extensions.Logging;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

internal sealed class ListWorkspacesHandler(AxGateway ax, ILogger<ListWorkspacesHandler> logger)
    : IRequestHandler<ListWorkspacesQuery, Outcome<IReadOnlyList<SandboxWorkspace>>>
{
    public Task<Outcome<IReadOnlyList<SandboxWorkspace>>> Handle(ListWorkspacesQuery query, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () => Outcome<IReadOnlyList<SandboxWorkspace>>.Ok(
            (await ax.ListWorkspacesAsync(ct)).Select(AxMapper.ToSandboxWorkspace).ToList()));
}
