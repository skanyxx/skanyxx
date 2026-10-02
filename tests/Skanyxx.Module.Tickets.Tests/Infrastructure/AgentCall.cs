namespace Skanyxx.Module.Tickets.Tests.Infrastructure;

/// <summary>What the fake kagent received: which agent, the exact prompt, and the headers that matter.</summary>
public sealed record AgentCall(string Namespace, string Agent, string Prompt, string Method, string? A2AVersion, string? UserId, bool Blocking);
