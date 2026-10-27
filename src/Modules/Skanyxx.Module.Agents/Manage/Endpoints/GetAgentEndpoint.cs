using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>One agent; <c>404</c> also when the caller may not read it. The system prompt only for builders and up.</summary>
internal sealed class GetAgentEndpoint(IMediator mediator) : Endpoint<AgentRef>
{
    public override void Configure()
    {
        Get("{namespace}/{name}");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
    }

    public override async Task HandleAsync(AgentRef req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new GetAgentQuery(StudioUser.From(User), req.Namespace, req.Name), ct)).ToHttp());
}
