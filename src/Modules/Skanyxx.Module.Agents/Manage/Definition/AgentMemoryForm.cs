namespace Skanyxx.Module.Agents.Manage.Definition;

/// <summary>kagent's TTL memory (<c>declarative.memory</c>): the ModelConfig whose embeddings it stores, and how many days an entry lives.</summary>
internal sealed record AgentMemoryForm(string ModelConfig, int TtlDays);
