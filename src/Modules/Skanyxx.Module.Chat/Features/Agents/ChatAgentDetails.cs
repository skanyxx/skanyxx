using Skanyxx.Core.Platform.Memory;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <summary>
/// The right-hand panel: what the agent is and can reach. Never its system prompt (the Agents page shows that to
/// builders and up). <paramref name="Memory"/> is null without the memory module; empty means memory's default.
/// <paramref name="Price"/> is null when no price is configured for the agent's model (Chat then shows n/a).
/// </summary>
internal sealed record ChatAgentDetails(
    string Namespace,
    string Name,
    string Description,
    bool Ready,
    string? Model,
    string? ModelProvider,
    string? ModelConfig,
    bool AcceptsImages,
    IReadOnlyList<ChatToolServer> ToolServers,
    IReadOnlyList<string> AgentTools,
    IReadOnlyList<string> Skills,
    IReadOnlyList<ChatAgentSkill> A2aSkills,
    IReadOnlyList<AgentMemoryGrant>? Memory,
    string AgentsPageUrl,
    ChatAgentPrice? Price = null);
