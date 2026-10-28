namespace Skanyxx.Module.Chat.Features.Agents;

/// <param name="Ref"><c>namespace/name</c> of the MCP server.</param>
/// <param name="AllTools">The agent may use every tool the server lists (its spec names none).</param>
internal sealed record ChatToolServer(string Ref, bool AllTools, IReadOnlyList<ChatTool> Tools);
