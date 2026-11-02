using Skanyxx.Core.Services;

namespace Skanyxx.Module.Chat.Features.Agents;

/// <param name="Ref"><c>namespace/name</c> of the MCP server.</param>
/// <param name="AllTools">The agent may use every tool the server lists (its spec names none).</param>
/// <param name="Problem">Null while it looks fine; else a <see cref="ToolServerHealth"/> text, <see cref="ToolServerHealth.Unavailable"/> for anyone but builders and up (D554).</param>
internal sealed record ChatToolServer(string Ref, bool AllTools, IReadOnlyList<ChatTool> Tools, string? Problem = null);
