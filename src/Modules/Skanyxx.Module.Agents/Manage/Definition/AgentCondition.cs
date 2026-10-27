namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>One of kagent's status conditions (Accepted, Ready), for people who manage the agent.</summary>
internal sealed record AgentCondition(string Type, string Status, string? Reason, string? Message);
