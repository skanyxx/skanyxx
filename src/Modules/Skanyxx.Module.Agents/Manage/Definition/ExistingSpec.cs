namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>What an agent already has that the form may keep unchanged even when today's rules would not allow it.</summary>
internal sealed record ExistingSpec(IReadOnlyDictionary<string, HashSet<string>> McpTools, IReadOnlySet<string> Skills);
