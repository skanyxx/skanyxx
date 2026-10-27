using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary><c>409</c> for a studio or system agent (change it in Studio), a BYO agent, or a prompt kept elsewhere.</summary>
internal sealed class UpdateAgentEndpoint(IMediator mediator) : Endpoint<AgentForm>
{
    public override void Configure()
    {
        Put("{namespace}/{name}");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(AgentForm req, CancellationToken ct)
    {
        // The route names the agent, whatever the body says.
        req.Namespace = Route<string>("namespace")!;
        req.Name = Route<string>("name")!;
        await Send.ResultAsync((await mediator.Send(new UpdateAgentCommand(StudioUser.From(User), req), ct)).ToHttp());
    }
}
