using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>Every agent for builders, supervisors and the owner; the agents Chat offers for employees.</summary>
internal sealed class ListAgentsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/" + AgentsGroup.Prefix);
        AgentsGroup.Apply(Definition);
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ListAgentsQuery(StudioUser.From(User)), ct)).ToHttp());
}
