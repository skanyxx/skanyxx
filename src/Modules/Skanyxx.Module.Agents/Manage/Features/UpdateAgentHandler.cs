using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Services;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// D251/D252: a studio or system agent is refused (audited) before the form is even read; otherwise the form replaces
/// its fields in the current spec, and kagent's PUT replaces the spec (labels stay).
/// </summary>
internal sealed class UpdateAgentHandler(KAgentApiClient kagent, IMediator mediator, IOptions<StudioOptions> options, ILogger<UpdateAgentHandler> logger)
    : IRequestHandler<UpdateAgentCommand, Outcome<AgentDetail>>
{
    public async Task<Outcome<AgentDetail>> Handle(UpdateAgentCommand command, CancellationToken ct)
    {
        if (!AgentAccess.CanWrite(command.User))
            return Outcome<AgentDetail>.Forbidden(AgentAccess.NotWriter);
        var form = command.Form;
        if (!AgentQueries.IsRef(form.Namespace, form.Name))
            return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
        if (form.Namespace != options.Value.Namespace)
            return Outcome<AgentDetail>.Conflict(null, AgentFailure.OtherNamespace(options.Value.Namespace), "namespace");
        try
        {
            var existing = await kagent.GetAgentObjectAsync(form.Namespace, form.Name, ct);
            if (existing is null)
                return Outcome<AgentDetail>.NotFound(AgentFailure.NotFound);
            var labels = AgentSpec.Labels(existing);
            if (AgentProtection.Refusal(form.Name, labels) is { } refusal)
            {
                await AgentAudit.WriteAsync(mediator, AgentAudit.Refused, command.User, form.Namespace, form.Name, reason: "update: " + AgentProtection.SourceOf(form.Name, labels));
                return Outcome<AgentDetail>.Conflict(null, refusal, AgentProtection.SourceOf(form.Name, labels));
            }
            if (!AgentSpec.IsDeclarative(existing))
                return Outcome<AgentDetail>.Conflict(null, AgentFailure.NotDeclarative, "byo");
            if (AgentSpec.HasPromptSource(existing))
                return Outcome<AgentDetail>.Conflict(null, AgentFailure.PromptSource, "prompt-source");
            if (AgentSpec.HasUneditableTools(existing))
                return Outcome<AgentDetail>.Conflict(null, AgentFailure.UneditableTools, "tools");

            var problems = AgentFormRules.Check(form, options.Value, AgentSpec.Existing(existing),
                await AgentQueries.ModelsAsync(kagent, options.Value.Namespace, ct),
                AgentQueries.Callable(await AgentQueries.AgentsAsync(kagent, options.Value.Namespace, ct)));
            if (problems.Count > 0)
                throw AgentFailure.Invalid(problems);

            var updated = AgentSpec.Updated(existing, form);
            await kagent.UpdateAgentAsync(updated, ct);
            await AgentAudit.WriteAsync(mediator, AgentAudit.Updated, command.User, form.Namespace, form.Name, form);
            logger.LogWarning("Agent {Namespace}/{Name} updated by {ActorUserId}", form.Namespace, form.Name, command.User.UserId);
            return Outcome<AgentDetail>.Ok(AgentSpec.Detail(updated, full: true, canWrite: true));
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            logger.LogWarning(ex, "Agent {Namespace}/{Name}: kagent did not update it", form.Namespace, form.Name);
            return Outcome<AgentDetail>.Unavailable(AgentFailure.Unavailable);
        }
    }
}
