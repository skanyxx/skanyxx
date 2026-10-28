using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>
/// D312: <c>201</c> with the Studio proposal's number; nothing reaches kagent. Builders and employees only (a supervisor
/// saves directly, <c>403</c>); <c>409</c> <c>studio-not-configured</c> without the agent repo; <c>400</c> keyed by field.
/// </summary>
internal sealed class ProposeAgentFormEndpoint(IMediator mediator) : Endpoint<AgentForm>
{
    public override void Configure()
    {
        Post("proposals");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Builder, SkanyxxRoles.Employee);
    }

    public override async Task HandleAsync(AgentForm req, CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new ProposeAgentFormCommand(StudioUser.From(User), req), ct)).ToHttp());
}
