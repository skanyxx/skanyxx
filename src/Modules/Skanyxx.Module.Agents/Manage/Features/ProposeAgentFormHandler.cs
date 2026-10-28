using MediatR;
using Microsoft.Extensions.Options;
using Skanyxx.Core.Platform;
using Skanyxx.Core.Platform.Studio;
using Skanyxx.Module.Agents.Manage.Definition;
using Skanyxx.Module.Agents.Studio;
using Skanyxx.Module.Agents.Studio.Definition;
using Skanyxx.Module.Agents.Studio.Features;

namespace Skanyxx.Module.Agents.Manage.Features;

/// <summary>
/// D312: builders and employees submit the Agents form and get a Studio pull request, never a live change. The form
/// becomes a studio draft: what a studio agent cannot hold (agent tools, A2A skills, a runtime, streaming, memory on
/// another model) is refused at its field; a change to an agent in main keeps its memory grants, which the form does
/// not show. The studio's own rules, gates and limits then decide as for any proposal.
/// </summary>
internal sealed class ProposeAgentFormHandler(IMediator mediator, IOptions<StudioOptions> options)
    : IRequestHandler<ProposeAgentFormCommand, Outcome<StudioResult>>
{
    public async Task<Outcome<StudioResult>> Handle(ProposeAgentFormCommand command, CancellationToken ct)
    {
        var (user, form) = (command.User, command.Form);
        if (!AgentAccess.CanProposeHere(user))
            return Outcome<StudioResult>.Forbidden(AgentAccess.Writer);
        if (!options.Value.Configured)
            return StudioFailure.Conflict<StudioResult>(StudioFailure.NotConfigured, AgentFailure.StudioNotConfigured);
        var problems = Problems(form);
        if (problems.Count > 0)
            throw AgentFailure.Invalid(problems);

        var main = await mediator.Send(new EditDraftQuery(user, form.Name, FromAgentsPage: true), ct);
        if (main.Status is not (OutcomeStatus.Ok or OutcomeStatus.NotFound))
            return new Outcome<StudioResult>(main.Status, Message: main.Message, Reason: main.Reason);
        var draft = new AgentDraft(form.Name, form.Description, form.ModelConfig, form.SystemMessage, form.Skills, form.McpTools,
            main.Value?.Grants ?? [], form.Memory?.TtlDays);
        return await mediator.Send(new ProposeAgentCommand(user, draft.Normalized(), FromAgentsPage: true), ct);
    }

    private List<FormProblem> Problems(AgentForm form)
    {
        var problems = new List<FormProblem>();
        if (form.Namespace != options.Value.Namespace)
            problems.Add(new("namespace", $"Namespace: agents are proposed in '{options.Value.Namespace}' only."));
        if (!AgentFormRules.IsAgentName(form.Name))
            problems.Add(new("name", "Name: lowercase letters, digits and '-', starting with a letter and ending with a letter or digit."));
        else if (StudioNames.IsReserved(form.Name))
            problems.Add(new("name", $"Name '{form.Name}' is reserved for Skanyxx's own agents (seed, skanyxx-*, preview-*)."));
        if (form.AgentTools is { Count: > 0 })
            problems.Add(new("agentTools", "A proposal cannot call other agents as tools: Studio agents never do."));
        if (form.A2aSkills is { Count: > 0 })
            problems.Add(new("a2aSkills", "A proposal carries no A2A card skills: Studio agents advertise none."));
        if (!string.IsNullOrEmpty(form.Runtime))
            problems.Add(new("runtime", "A proposal runs on kagent's default runtime; leave Runtime empty."));
        if (form.Stream)
            problems.Add(new("runtime", "A proposal does not stream responses; leave streaming off."));
        if (form.Memory is { } memory && memory.ModelConfig != form.ModelConfig)
            problems.Add(new("memory", "In a proposal, memory uses the agent's own model; pick the same model for both."));
        return problems;
    }
}
