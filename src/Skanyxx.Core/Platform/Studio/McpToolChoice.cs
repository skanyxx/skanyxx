namespace Skanyxx.Core.Platform.Studio;

/// <summary>One allow-listed RemoteMCPServer (other than memory) and the tools the agent may call on it.</summary>
public sealed record McpToolChoice(string Server, IReadOnlyList<string> Tools);
