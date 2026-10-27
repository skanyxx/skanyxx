using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Manage.Definition;

/// <param name="Namespace">Where agents are created (<c>Studio:Namespace</c>).</param>
/// <param name="ModelConfigs">ModelConfig names in that namespace.</param>
/// <param name="McpServers">The allow-listed RemoteMCPServers with the tools kagent discovered.</param>
/// <param name="Agents">Agents an agent may call as a tool.</param>
/// <param name="SkillRegistries">Where a skill may come from; empty = skills are off.</param>
internal sealed record AgentFormOptions(
    string Namespace, IReadOnlyList<string> ModelConfigs, IReadOnlyList<McpToolChoice> McpServers, IReadOnlyList<string> Agents,
    IReadOnlyList<string> SkillRegistries);
