using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// Branch + one commit (<c>agents/&lt;name&gt;/agent.yaml</c> and <c>grants.yaml</c>) + a pull request against
/// <c>skanyxx-agents</c> (D020, D028, D029). Nothing reaches kagent: a supervisor's merge does (D024, D045).
/// </summary>
/// <param name="FromAgentsPage">Sent by the Agents page for someone who may not change a live agent (D312): employees propose too.</param>
/// <param name="Direct">The first half of a supervisor's or the owner's direct apply (D366), which merges it at once.</param>
public sealed record ProposeAgentCommand(StudioUser User, AgentDraft Draft, bool FromAgentsPage = false, bool Direct = false) : IRequest<Outcome<StudioResult>>
{
    public bool MayPropose => User.CanPropose || FromAgentsPage || (Direct && User.CanMerge);
}
