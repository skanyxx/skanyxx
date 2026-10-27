namespace Skanyxx.Module.ChatContext.Extraction;

/// <summary>What the person typed (without injected context) and what the agent answered.</summary>
public sealed record ChatTurn(string Message, string Reply);
