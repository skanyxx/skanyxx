using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>D257: scale the agent to zero and take it out of Chat. <c>409</c> for studio and system agents.</summary>
internal sealed class SuspendAgentEndpoint(IMediator mediator) : Endpoint<AgentRef>
{
    public override void Configure()
    {
        Post("{namespace}/{name}/suspend");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(AgentRef req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SetAgentSuspendedCommand(StudioUser.From(User), req.Namespace, req.Name, true), ct)).ToHttp());
}
