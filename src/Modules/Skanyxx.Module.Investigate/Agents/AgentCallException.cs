namespace Skanyxx.Module.Investigate.Agents;

/// <summary>No usable answer came back. The message is a fixed category, never kagent's or the model's own text.</summary>
public sealed class AgentCallException(string message) : Exception(message);
