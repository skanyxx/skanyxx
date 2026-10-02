namespace Skanyxx.Module.Tickets.Engine;

/// <summary>The agent could not be reached or gave no usable answer. The message never carries a credential.</summary>
public sealed class StageAgentException(string message) : Exception(message);
