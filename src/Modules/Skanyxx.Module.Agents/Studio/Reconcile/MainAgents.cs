using Skanyxx.Core.Platform.Studio;

namespace Skanyxx.Module.Agents.Studio.Reconcile;

/// <param name="Valid">The valid agents among the folders read.</param>
/// <param name="ServerUsers">
/// Each MCP server the folders' files name (valid or not) → the agents naming it: a plugin server with a user must not
/// leave (D219).
/// </param>
internal sealed record MainAgents(Dictionary<string, AgentDraft> Valid, Dictionary<string, SortedSet<string>> ServerUsers);
