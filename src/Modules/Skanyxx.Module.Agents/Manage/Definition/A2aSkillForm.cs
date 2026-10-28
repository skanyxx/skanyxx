namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>One skill of the agent's A2A card, as kagent's <c>AgentSkill</c>.</summary>
internal sealed record A2aSkillForm(string Id, string Name, string? Description, List<string>? Tags, List<string>? Examples);
