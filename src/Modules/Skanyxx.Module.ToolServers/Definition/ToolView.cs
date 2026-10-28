namespace Skanyxx.Module.ToolServers.Definition;

/// <summary>A tool kagent discovered on a server (the server's own text: the page writes it as text, never markup).</summary>
public sealed record ToolView(string Name, string Description);
