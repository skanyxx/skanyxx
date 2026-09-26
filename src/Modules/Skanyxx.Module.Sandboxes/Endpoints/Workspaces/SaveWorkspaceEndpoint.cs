using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Module.Sandboxes.Features.Workspaces;

namespace Skanyxx.Module.Sandboxes.Endpoints.Workspaces;

internal sealed class SaveWorkspaceEndpoint(IMediator mediator) : Endpoint<SaveWorkspaceRequest>
{
    public override void Configure()
    {
        Put("workspaces/{name}");
        Group<SandboxesGroup>();
    }

    public override async Task HandleAsync(SaveWorkspaceRequest req, CancellationToken ct)
    {
        var outcome = await mediator.Send(new SaveWorkspaceCommand(SandboxesHeaders.User(HttpContext), req.Name,
            req.Git ?? [], req.McpServers ?? [], req.AttachMemory), ct);
        await Send.ResultAsync(outcome.ToHttp(workspace => workspace));
    }
}
