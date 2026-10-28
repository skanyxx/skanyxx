using FastEndpoints;
using MediatR;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Features;

namespace Skanyxx.Module.Agents.Manage.Endpoints;

/// <summary>Models, allow-listed MCP servers with their tools, agents and skill registries for the form (every role, D312).</summary>
internal sealed class AgentFormOptionsEndpoint(IMediator mediator) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("options");
        Group<AgentsGroup>();
        Roles(SkanyxxRoles.Owner, SkanyxxRoles.Supervisor, SkanyxxRoles.Builder, SkanyxxRoles.Employee);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.ResultAsync((await mediator.Send(new AgentFormOptionsQuery(StudioUser.From(User)), ct)).ToHttp());
}
