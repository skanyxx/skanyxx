namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>A tool's name and the description its MCP server gives (empty when kagent discovered none).</summary>
internal sealed record ToolInfo(string Name, string Description);
