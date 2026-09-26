using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Domain;
using Skanyxx.Module.Sandboxes.Gateway;

namespace Skanyxx.Module.Sandboxes.Features.Workspaces;

internal sealed class SaveWorkspaceHandler(AxGateway ax, IOptions<SandboxesOptions> options, ILogger<SaveWorkspaceHandler> logger)
    : IRequestHandler<SaveWorkspaceCommand, Outcome<SandboxWorkspace>>
{
    public Task<Outcome<SandboxWorkspace>> Handle(SaveWorkspaceCommand command, CancellationToken ct) =>
        AxErrors.Guard(logger, ct, async () =>
        {
            if (!options.Value.Supervisors.Contains(command.UserId))
                return Outcome<SandboxWorkspace>.Forbidden("Only a supervisor may write workspaces; they are shared by every task that binds them.");

            var workspace = AxMapper.ToAxWorkspace(command.Name, command.Git, command.McpServers,
                command.AttachMemory ? options.Value.MemoryMcpUrl : null);
            return Outcome<SandboxWorkspace>.Ok(AxMapper.ToSandboxWorkspace(await ax.UpdateWorkspaceAsync(workspace, ct)));
        });
}
