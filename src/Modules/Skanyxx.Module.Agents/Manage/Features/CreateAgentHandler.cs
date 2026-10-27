using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>D251: role, then the form against the namespace's models and agents, then kagent; audited (D255).</summary>
internal sealed class CreateAgentHandler(KAgentApiClient kagent, IMediator mediator, IOptions<StudioOptions> options, ILogger<CreateAgentHandler> logger)
    : IRequestHandler<CreateAgentCommand, Outcome<AgentDetail>>
{
    public async Task<Outcome<AgentDetail>> Handle(CreateAgentCommand command, CancellationToken ct)
    {
        if (!AgentAccess.CanWrite(command.User))
            return Outcome<AgentDetail>.Forbidden(AgentAccess.NotWriter);
        var form = command.Form;
        try
        {
            var agents = await AgentQueries.AgentsAsync(kagent, options.Value.Namespace, ct);
            var problems = AgentFormRules.Check(form, options.Value, null, await AgentQueries.ModelsAsync(kagent, options.Value.Namespace, ct), agents);
            if (problems.Count > 0)
                throw AgentFailure.Invalid(problems);
            if (agents.Contains(form.Name))
                return Outcome<AgentDetail>.Conflict(null, AgentFailure.Exists(form.Namespace, form.Name), "exists");

            var agent = AgentSpec.NewAgent(form);
            await kagent.CreateAgentAsync(agent, ct);
            await AgentAudit.WriteAsync(mediator, AgentAudit.Created, command.User, form.Namespace, form.Name, form);
            logger.LogWarning("Agent {Namespace}/{Name} created by {ActorUserId}", form.Namespace, form.Name, command.User.UserId);
            return Outcome<AgentDetail>.Created(AgentSpec.Detail(agent, full: true, canWrite: true));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not create it", form.Namespace, form.Name);
            return Outcome<AgentDetail>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
