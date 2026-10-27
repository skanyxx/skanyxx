using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary><c>409</c> for a studio or system agent: the seed and the factory never leave kagent from here.</summary>
internal sealed class DeleteAgentEndpoint(IMediator mediator) : Endpoint<AgentRef>
{
    public override void Configure()
    {
        Delete("{namespace}/{name}");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(AgentRef req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new DeleteAgentCommand(StudioUser.From(User), req.Namespace, req.Name), ct)).ToHttp());
}
