using Skanyxx.Module.Investigate.Agents;

namespace Skanyxx.Module.Investigate.Contracts;

/// <summary>
/// What the editor offers: the agents a step may call (<paramref name="AgentsAvailable"/> false when kagent did not
/// answer), the teams a workflow may be shared with, and the limits.
/// </summary>
public sealed record OptionsDto(IReadOnlyList<AgentChoice> Agents, bool AgentsAvailable, IReadOnlyList<TeamChoice> Teams, LimitsDto Limits);
