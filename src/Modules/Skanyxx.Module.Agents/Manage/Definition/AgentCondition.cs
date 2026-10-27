namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>
/// One of kagent's status conditions (Accepted, Ready), for people who manage the agent. Never its message: that is
/// free text from kagent and Kubernetes and may quote a header or a Secret (C-4, requirements.md §1.4).
/// </summary>
internal sealed record AgentCondition(string Type, string Status, string? Reason);
