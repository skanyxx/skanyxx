using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>D257: back to kagent's default replica count and back in Chat.</summary>
internal sealed class ResumeAgentEndpoint(IMediator mediator) : Endpoint<AgentRef>
{
    public override void Configure()
    {
        Post("{namespace}/{name}/resume");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(AgentRef req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new SetAgentSuspendedCommand(StudioUser.From(User), req.Namespace, req.Name, false), ct)).ToHttp());
}
