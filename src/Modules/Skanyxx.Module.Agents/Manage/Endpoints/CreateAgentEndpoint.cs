using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary><c>201</c> created in kagent; <c>400</c> the form's problems; <c>409</c> the name is taken.</summary>
internal sealed class CreateAgentEndpoint(IMediator mediator) : Endpoint<AgentForm>
{
    public override void Configure()
    {
        Post("/" + AgentsGroup.Prefix);
        AgentsGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor);
    }

    public override async Task HandleAsync(AgentForm req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new CreateAgentCommand(StudioUser.From(User), req), ct)).ToHttp());
}
