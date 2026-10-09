using MediatR;

namespace Skanyxx.Core.Platform.Studio;

/// <summary>
/// Branch + one commit (<c>agents/&lt;name&gt;/agent.yaml</c> and <c>grants.yaml</c>) + a pull request against
/// <c>skanyxx-agents</c> (D020, D028, D029). Nothing reaches kagent: a supervisor's merge does (D024, D045).
/// </summary>
public sealed record ProposeAgentCommand(StudioUser User, AgentDraft Draft) : IRequest<Outcome<StudioResult>>;
