namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>An MCP server the agent uses and its tools with the descriptions kagent discovered (as kagent-ui groups them).</summary>
internal sealed record ServerTools(string Server, IReadOnlyList<ToolInfo> Tools);
